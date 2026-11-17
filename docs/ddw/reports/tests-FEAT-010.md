# Test run FEAT-010

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de punta a
punta), sobre los 8 bloques de la spec (dominio y persistencia de la ubicación, el historial y la retención; casos de
uso y Api de la ubicación; pantalla de la ubicación; copia, verificación, historial y retención; programación diaria y
al iniciar; estado y aviso al ingresar; restauración por comando; e2e) más las correcciones del SAST (D-38). Las etapas
corrieron una detrás de otra, sin compilar en paralelo con la cobertura web.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet build Optica.sln && dotnet test Optica.sln --no-build --collect:"XPlat Code Coverage" && (cd web && npm run lint && npm run test:lint && npm run test:coverage && npm run build) && (cd e2e && npm run test:unidad && npm run test:harness) && dotnet format Optica.sln --verify-no-changes` |
| Total | 4523 |
| Passed | 4523 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 96.7% |
| Branch coverage | 89.4% |
| Function coverage | 97.6% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean, 0 findings; `dotnet build Optica.sln` — 0 warnings, 0 errors; `npm run lint` en `web/` (oxlint `--max-warnings 0`) — clean, 0 findings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors; `npm run test:lint` — 8/8 |

Desglose: Optica.Domain.Tests 513, Optica.Application.Tests 617, Optica.Infrastructure.Tests 945, Optica.Api.Tests 744
(.NET 2819); web 1335 (27 archivos); e2e unidad 159; e2e 210 (105 en Chrome y 105 en Edge, 48 proyectos, con
`configuracion-backup` y `backup-aviso` nuevos en los dos navegadores). Las 8 pruebas de `npm run test:lint` verifican
la configuración del lint y se informan en la fila Lint, fuera del total. La suite de la Api tardó 1 min 19 s y el
harness e2e 6,3 min. La web, la e2e y el lint corrieron primero sobre `af9f19b`; después de las correcciones del SAST
(solo `src/` y `tests/` de .NET) se repitieron .NET con cobertura, el formato, la e2e de unidad y el harness completo,
con los mismos resultados.

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet con la ruta de cada archivo resuelta completa,
sin las migraciones ni los archivos generados: líneas 16734/17300, ramas 3802/4255, funciones 1802/1846. Web, por
separado: sentencias 99 %, ramas 97,13 %, funciones 100 %, líneas 98,95 %. Las pruebas de unidad de e2e y la e2e no
miden cobertura.

Cobertura .NET de los 32 archivos de producción del ticket (nuevos o modificados, sin migraciones): líneas 4288/4426
(96,9 %), ramas 597/681 (87,7 %), funciones 387/392 (98,7 %). Por archivo, por debajo del 80 % de ramas:
`ConfiguracionController.cs` 33/42 (traducción de infracciones de configuraciones anteriores), `CopiadorBackupSqlite.cs`
2/4 y `OpticaDbContext.cs` 3/4. Ninguno baja del 80 % de líneas.

Fuera de la suite: la versión anterior de Chrome y Edge (D-02), el comportamiento de las reglas de unidades en Windows
(D-31) y la medición de la restauración en menos de 4 horas en la PC del local (D-14) quedan para la verificación manual
del usuario.

## Failures
(none)

## Skips
(none)
