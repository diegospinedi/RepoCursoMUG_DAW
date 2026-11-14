# Test run FEAT-007

Corrida del cierre de CODE: la suite completa del proyecto (.NET, web, pruebas de unidad de e2e y e2e de punta a
punta), sobre el commit `46e8b2e`, con los 8 bloques de la spec (configuración con las alícuotas de ARCA y el punto de
venta, su pantalla, dominio de facturación, persistencia con triggers, puerto de ARCA y simulador, cliente de WSAA y
WSFEv1, caso de uso y endpoint de emisión, pantalla de facturación y e2e) y la guía de instalación.

| Field | Value |
|---|---|
| Runner | dotnet test (SDK 10) con xUnit y coverlet; Vitest con cobertura v8 (web); node:test (e2e unidad); Playwright con Chrome y Edge estables |
| Command | `dotnet test Optica.sln --collect:"XPlat Code Coverage" && (cd web && npm run test:coverage && npm run build && npm run lint && npm run test:lint) && (cd e2e && npm run test:unidad && npm run test:harness) && dotnet format Optica.sln --verify-no-changes` |
| Total | 3478 |
| Passed | 3478 |
| Failed | 0 |
| Skipped | 0 |
| Line coverage | 96.3% |
| Branch coverage | 91.7% |
| Function coverage | 97.0% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean, 0 findings (después de corregir 26 avisos de espacios en 3 archivos de prueba, ver abajo); `dotnet build Optica.sln` — 0 warnings, 0 errors; `npm run lint` en `web/` (oxlint `--max-warnings 0`) — clean, 0 findings; `npm run build` en `web/` (tsc + vite) — clean, 0 errors; `npm run test:lint` — 8/8 |

Desglose: Optica.Domain.Tests 434, Optica.Application.Tests 463, Optica.Infrastructure.Tests 728, Optica.Api.Tests 592
(.NET 2217); web 1023; e2e unidad 80; e2e 158 (79 en Chrome y 79 en Edge, 28 proyectos, con `facturacion`,
`facturacion-rechazo` y `facturacion-sin-respuesta` nuevos en los dos navegadores). Las 8 pruebas de `npm run test:lint`
verifican la configuración del lint y se informan en la fila Lint, fuera del total. La suite de la Api tardó 16 min 15 s
con el recolector de cobertura y el harness e2e 3,4 min.

Formato: la primera corrida de `dotnet format --verify-no-changes` informó 26 avisos WHITESPACE en
`tests/Optica.Infrastructure.Tests/Persistencia/FacturaRepositoryTests.cs` (sangría de un inicializador),
`tests/Optica.Infrastructure.Tests/Arca/ClienteWsaaTests.cs` y `tests/Optica.Infrastructure.Tests/Arca/EscenarioArca.cs`
(miembros de tipos anónimos en una sola línea). Se corrigieron con ediciones de solo espacios; la segunda corrida de
`dotnet format --verify-no-changes` terminó sin avisos y `Optica.Infrastructure.Tests` volvió a pasar 728/728.

Cobertura de la tabla: .NET, unión de los cuatro informes de coverlet con la ruta de cada archivo resuelta completa y
sin las migraciones: líneas 7387/7672, ramas 2412/2629, funciones 1144/1179. Web, por separado: líneas 99,01 %, ramas
96,76 %, funciones 100 %, sentencias 99,06 %. Las pruebas de unidad de e2e y la e2e no miden cobertura.

Cobertura .NET de los archivos de producción del ticket (nuevos o modificados), con el mismo cálculo: líneas
3508/3682 (95,3 %), ramas 959/1091 (87,9 %), funciones 541/551 (98,2 %). Por archivo, por debajo del 80 % de ramas:
`ConfiguracionController.cs` 12/18, `ResultadoAutorizacion.cs` 6/12 y `ResultadoEmision.cs` 7/12 (guardas de
argumentos de fábricas cerradas), `AutorizadorNoConfigurado.cs` 2/4, `AutorizadorWsfe.cs` 9/12, `CertificadoArca.cs`
3/4 (con 71,4 % de líneas: ramas de `FileNotFoundException` y de macOS que no se dan en Linux), `ClienteWsaa.cs` 70/92,
`ServiciosArca.cs` 28/40, `FabricaConexion.cs` 23/30 (ramas de FEAT-001 que dependen del sistema operativo),
`OpticaDbContext.cs` 3/4 y `TextosFacturacion.cs` 14/18. Ninguno baja del 70 % de líneas salvo `CertificadoArca.cs`.

Rendimiento: AC-25 (NFR-02) por la Api, 20 emisiones con un autorizador que demora y registra su espera: la diferencia
entre el tiempo total y la espera de ARCA estuvo entre 11 y 84 ms en la corrida del verificador de Block 7 (D-23).
NFR-01 (30 s) se prueba con reloj controlado en .NET; el e2e usa 2 s (D-60, D-65).

Pendiente fuera de la suite: AC-24 (CAE real en homologación) queda para la verificación manual del usuario con el
certificado de homologación (D-02).

## Failures
(none)

## Skips
(none)
