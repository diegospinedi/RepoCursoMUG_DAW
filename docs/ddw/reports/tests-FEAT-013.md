# Test run FEAT-013

Esta es la corrida del cierre de CODE. Abarca la suite completa del proyecto: .NET, web, pruebas de unidad de e2e y e2e de
punta a punta. Cubre los 5 bloques de la spec:
1. La base de la búsqueda en vivo.
2. Artículos y el buscador del presupuesto.
3. Buscar presupuestos y Facturas.
4. Configuración con campos de filtro.
5. Las e2e y la prueba de rendimiento.

Las etapas corrieron una detrás de otra el 2026-11-18. .NET compiló en una carpeta propia porque la Api de desarrollo
corre desde `src/Optica.Api/bin`.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet build Optica.sln --artifacts-path bin/artifacts-cierre && dotnet test Optica.sln --no-build --artifacts-path bin/artifacts-cierre --collect:"XPlat Code Coverage" && (cd web && npm run lint && npm run test:lint && npm run test:coverage && npm run build) && (cd e2e && npm run test:unidad && npm run test:harness) && dotnet format Optica.sln --verify-no-changes` |
| Total | 4864 |
| Passed | 4864 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 99.17% |
| Branch coverage | 97.55% |
| Function coverage | 100% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `npm run lint` en `web/` (oxlint `--max-warnings 0`) — clean, 0 findings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors; `npm run test:lint` — 8/8; `dotnet build Optica.sln` — 0 warnings, 0 errors; `dotnet format Optica.sln --verify-no-changes` — clean |

## Desglose

| Suite | Pruebas |
|---|---|
| .NET | 2930: Domain 538, Application 637, Infrastructure 977, Api 778 |
| Web | 1512, en 36 archivos |
| e2e de unidad | 164 |
| e2e de punta a punta | 258: 129 en Chrome y 129 en Edge, en 56 proyectos |

- La e2e de punta a punta tardó 9,0 minutos. Incluye `busqueda-en-vivo`, que es nueva y corre en los dos navegadores,
  y los 10 specs actualizados por D-18.
- .NET suma 2 pruebas respecto de FEAT-012:
  - `BusquedaCancelada_NoRegistraErrorNoControlado`, del Block 1, que cubre D-16.
  - `Volumen_BuscarArmazon_P95MenorA700ms`, del Block 5, que cubre D-17 y AC-13. Su p95 dio unos 50 ms.
- Las 8 pruebas de `npm run test:lint` verifican la configuración del lint. Se informan en la fila Lint y quedan fuera
  del total.

## Cobertura

La cobertura de la tabla es la de la web, que es donde está todo el código del ticket:

| Métrica | Valor |
|---|---|
| Sentencias | 99,21 % |
| Ramas | 97,55 % |
| Funciones | 100 % |
| Líneas | 99,17 % |

El backend no cambió: el ticket agrega solo pruebas .NET. Las pruebas de unidad de e2e y la e2e de punta a punta no
miden cobertura.

## Failures
(none)

## Skips
(none)
