# Test run FEAT-002c

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de
punta a punta) más el paso propio de volumen de D-21, sobre el commit `42a4451`, con los 6 bloques de la spec.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10.0.401) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright 1.64.1 con Chrome y Edge estables |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && dotnet test tests/Optica.Api.Tests --filter Categoria=Volumen && dotnet test tests/Optica.Infrastructure.Tests --filter Categoria=Volumen && (cd web && npm run test:coverage) && (cd e2e && npm run test:unidad && npm run test:harness)` |
| Total | 1761 |
| Passed | 1761 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 95.3% |
| Branch coverage | 91.4% |
| Function coverage | 94.9% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean, 0 findings; `dotnet build Optica.sln --no-incremental` — 0 warnings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors |

Desglose: Optica.Domain.Tests 252, Optica.Application.Tests 206, Optica.Infrastructure.Tests 394,
Optica.Api.Tests 337 (.NET 1189); web 455; e2e unidad 49; e2e 68 (`chrome-acceso` 5, `chrome-articulos` 11,
`chrome-articulos-edicion` 7, `chrome-configuracion` 7, `chrome-configuracion-recalculo` 4, `msedge-acceso` 5,
`msedge-articulos` 11, `msedge-articulos-edicion` 7, `msedge-configuracion` 7, `msedge-configuracion-recalculo` 4).
Las pruebas de los dos pasos de volumen ya están contadas en la suite .NET; se repiten aparte como evidencia de D-21.

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet sin las migraciones generadas
(líneas 3888/4079, ramas 1349/1476, funciones 613/646). Código nuevo o modificado de .NET en esta rama (16
archivos de `src/`): líneas 95.3%, ramas 88.0%, funciones 98.6%. Web, por separado: líneas 99.25%, ramas
96.91%, funciones 100%, sentencias 99.28%. Las pruebas de unidad de e2e y la e2e no miden cobertura.

Paso de volumen (D-21), `dotnet test tests/Optica.Api.Tests --filter Categoria=Volumen`, 1/1, p95 de la
respuesta HTTP completa con 10.000 artículos (D-02, límite 1 s): texto que no coincide 37,5 ms; texto que
coincide con todos 41,2 ms; sin texto, página 1 11,9 ms; sin texto, última página 16,0 ms.
`dotnet test tests/Optica.Infrastructure.Tests --filter Categoria=Volumen`, 1/1: recálculo de 10.000 artículos
con múltiplo 50 en 644 ms (límite 10 s).

Intermitencia conocida, fuera del alcance de FEAT-002c: el harness e2e se corrió 5 veces en el cierre; las
corridas 2, 3 y 5 pasaron 68/68 (la 5 es la de la tabla) y las corridas 1 y 4 tuvieron 1 falla cada una. En la
corrida 4 falló `[msedge-acceso] acceso.spec.ts:198 cinco intentos incorrectos bloquean el ingreso` (esperado
423, recibido 204 al ingresar con la contraseña correcta durante el bloqueo); la salida de la corrida 1 no se
guardó y su falla no está identificada. La causa probable (sin confirmar) es un retroceso del reloj del sistema
en WSL2: `PoliticaBloqueo.Evaluar` (FEAT-001b) toma el bloqueo como vencido si la hora actual queda antes del
quinto rechazo. Por decisión del usuario se trata en un ticket FIX aparte de FEAT-001b.

## Failures
(none)

## Skips
(none)
