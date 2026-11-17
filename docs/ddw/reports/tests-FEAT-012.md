# Test run FEAT-012

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de punta a punta),
sobre los 3 bloques de la spec (menú de tarjetas en Inicio; estilo uniforme en las 15 pantallas; e2e en Chrome y Edge).
El ticket cambia solo la web y las e2e; .NET se corre igual para confirmar que nada se rompió. Las etapas corrieron una
detrás de otra; .NET compiló en una carpeta propia porque la Api de desarrollo corre desde `src/Optica.Api/bin`.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet build Optica.sln --artifacts-path bin/artifacts-cierre && dotnet test Optica.sln --no-build --artifacts-path bin/artifacts-cierre --collect:"XPlat Code Coverage" && (cd web && npm run lint && npm run test:lint && npm run test:coverage && npm run build) && (cd e2e && npm run test:unidad && npm run test:harness) && dotnet format Optica.sln --verify-no-changes` |
| Total | 4762 |
| Passed | 4762 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 99.0% |
| Branch coverage | 97.3% |
| Function coverage | 100% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `npm run lint` en `web/` (oxlint `--max-warnings 0`) — clean, 0 findings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors; `npm run test:lint` — 8/8; `dotnet build Optica.sln` — 0 warnings, 0 errors; `dotnet format Optica.sln --verify-no-changes` — clean |

## Desglose

| Suite | Pruebas |
|---|---|
| .NET | 2928: Domain 538, Application 637, Infrastructure 977, Api 776 |
| Web | 1428, en 31 archivos |
| e2e de unidad | 164 |
| e2e de punta a punta | 242: 121 en Chrome y 121 en Edge, en 54 proyectos |

- La e2e de punta a punta incluye `inicio-menu` y `estilo-uniforme`, nuevos en los dos navegadores, y `backup-aviso` con
  el orden nuevo del menú. El harness tardó 7,6 minutos.
- Las 8 pruebas de `npm run test:lint` verifican la configuración del lint. Se informan en la fila Lint y quedan fuera
  del total.

## Cobertura

La cobertura de la tabla es la de la web, que es donde está todo el código del ticket:

| Métrica | Valor |
|---|---|
| Sentencias | 99,06 % |
| Ramas | 97,28 % |
| Funciones | 100 % |
| Líneas | 99,02 % |

La cobertura .NET no cambia respecto de FEAT-011: 96,8 / 89,7 / 97,8. Las pruebas de unidad de e2e y la e2e de punta a
punta no miden cobertura.

## Fuera de la suite

Queda para la verificación manual del usuario la versión anterior de Chrome y de Edge (D-02).

## Failures
(none)

## Skips
(none)
