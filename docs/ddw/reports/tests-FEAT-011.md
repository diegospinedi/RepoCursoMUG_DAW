# Test run FEAT-011

Corrida del cierre de CODE. Abarcó la suite completa del proyecto: .NET, web, pruebas de unidad de e2e y e2e de punta
a punta. La rama tiene los 6 bloques de la spec:
- reglas de la planilla en el dominio;
- lector del .xlsx con límites;
- caso de uso y persistencia;
- endpoint de la Api;
- pantalla "Actualizar costos";
- e2e en Chrome y Edge.

Las etapas corrieron una detrás de otra, en el orden del comando. La compilación de .NET usó una carpeta de salida
propia, porque la Api de desarrollo corre desde `src/Optica.Api/bin`.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet build Optica.sln --artifacts-path bin/artifacts-cierre && dotnet test Optica.sln --no-build --artifacts-path bin/artifacts-cierre --collect:"XPlat Code Coverage" && (cd web && npm run lint && npm run test:lint && npm run test:coverage && npm run build) && (cd e2e && npm run test:unidad && npm run test:harness) && dotnet format Optica.sln --verify-no-changes` |
| Total | 4705 |
| Passed | 4705 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 96.8% |
| Branch coverage | 89.7% |
| Function coverage | 97.8% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean, 0 findings; `dotnet build Optica.sln` — 0 warnings, 0 errors; `npm run lint` en `web/` (oxlint `--max-warnings 0`) — clean, 0 findings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors; `npm run test:lint` — 8/8 |

## Desglose

| Suite | Pruebas |
|---|---|
| Optica.Domain.Tests | 538 |
| Optica.Application.Tests | 637 |
| Optica.Infrastructure.Tests | 977 |
| Optica.Api.Tests | 776 |
| **.NET** | **2928** |
| web (28 archivos) | 1395 |
| e2e unidad | 164 |
| e2e de punta a punta | 218 |

- **e2e de punta a punta:** 109 en Chrome y 109 en Edge, en 50 proyectos. Incluye `actualizar-costos` en los dos
  navegadores; el harness tardó 7,8 min.
- **Lint:** las 8 pruebas de `npm run test:lint` verifican la configuración del lint. Se informan en la fila Lint y
  quedan fuera del total.
- **AC-13** (`Planilla10000Filas_Catalogo10000_MenosDe120SegundosY10EnTransaccion`, en Infrastructure.Tests): en el
  cierre del Block 4 midió 1,5 s en total y 1,2 s dentro de la transacción. Los umbrales son 120 s y 10 s.

## Cobertura

### Cobertura de la tabla

- **.NET:** es la unión de los cuatro informes de coverlet, con la ruta de cada archivo resuelta completa. Quedan
  afuera las migraciones y los archivos generados. Líneas 17749/18345, ramas 4337/4836, funciones 1945/1989.
- **Web,** por separado: sentencias 99,05 %, ramas 97,26 %, funciones 100 %, líneas 99 %.
- Las pruebas de unidad de e2e y la e2e no miden cobertura.

### Cobertura .NET del ticket

Abarca los 13 archivos de producción del ticket que tienen líneas instrumentadas, nuevos o modificados:

| Métrica | Cubiertas | Porcentaje |
|---|---|---|
| Líneas | 1710/1784 | 95,9 % |
| Ramas | 712/771 | 92,3 % |
| Funciones | 196/197 | 99,5 % |

Por archivo, ninguno baja del 80 % de líneas. Por debajo del 80 % de ramas quedan:
- `CandadoCargaPlanilla.cs`: 3/4.
- `ILectorPlanillaCostos.cs`: 1/2.

Los dos son tipos de pocas ramas.

## Fuera de la suite

Quedan para la verificación manual del usuario:
- la versión anterior de Chrome y Edge (D-02);
- una planilla real de un proveedor exportada desde Excel o LibreOffice.

## Failures
(none)

## Skips
(none)
