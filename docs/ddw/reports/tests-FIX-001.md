# Test run FIX-001

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de punta a
punta) sobre el commit `307fc07`, con el arreglo de `PoliticaBloqueo` y sus pruebas de regresión. El harness e2e se
corrió 5 veces seguidas como evidencia de AC-05.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright 1.64.1 con Chrome y Edge estables |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run test:coverage) && (cd e2e && npm run test:unidad && npm run test:harness)` |
| Total | 1769 |
| Passed | 1769 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 95.3% |
| Branch coverage | 91.4% |
| Function coverage | 94.9% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors |

Desglose: Optica.Domain.Tests 256, Optica.Application.Tests 209, Optica.Infrastructure.Tests 394, Optica.Api.Tests 338
(.NET 1197); web 455; e2e unidad 49; e2e 68 (`chrome-acceso` 5, `chrome-articulos` 11, `chrome-articulos-edicion` 7,
`chrome-configuracion` 7, `chrome-configuracion-recalculo` 4, y los mismos cinco proyectos `msedge-*`).

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet sin las migraciones generadas (líneas 3890/4081,
ramas 1349/1476, funciones 613/646). Archivos de `src/` modificados por el arreglo (`PoliticaBloqueo.cs` e
`IngresarConContrasena.cs`): líneas 98.0% (96/98), ramas 94.1% (32/34), funciones 100% (21/21). Web, por separado:
líneas 99.25%, ramas 96.91%, funciones 100%. Las pruebas de unidad de e2e y la e2e no miden cobertura.

AC-05: el harness completo pasó 68/68 en 5 corridas seguidas (1,4 a 1,5 min cada una), sin reintentos; la prueba
`cinco intentos incorrectos bloquean el ingreso` pasó en Chrome y Edge en las 5.

## Failures
(none)

## Skips
(none)
