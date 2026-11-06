# Verification FIX-001

Verificación cruzada del arreglo (ronda 1), sobre `7e66e15` (arreglo en `307fc07`), por un verificador que no escribió el
código, con corridas propias en una copia del repositorio: .NET 1197/1197, web 455/455, e2e unidad 49/49 y el harness
e2e 68/68 en dos corridas más (además de las 5 del cierre de CODE).

| Field | Value |
|---|---|
| Module | `src/Optica.Domain/Acceso/PoliticaBloqueo.cs` (`Evaluar`, `ToleranciaRetroceso`) y `src/Optica.Application/Acceso/IngresarConContrasena.cs` (comentarios) |
| Line coverage | 100% |
| Branch coverage | 100% |
| Function coverage | 100% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet build Optica.sln --no-incremental` — 0 warnings; `dotnet format Optica.sln --verify-no-changes --no-restore` — clean; `npm run build` en `web/` — clean |

Cobertura de la tabla: los dos archivos de `src/` modificados, uniendo las rutas con que coverlet registra cada proyecto
de pruebas (líneas 98/98, ramas 34/34, funciones 21/21). El informe de pruebas da 98,0 / 94,1 / 100 porque su script no
contaba la ruta del proyecto de dominio; es una diferencia de método, no de pruebas.

## Acceptance criteria
- ✅ AC-01 — `PoliticaBloqueo_RelojAtrasado1Segundo_SigueBloqueado`, `PoliticaBloqueo_SeisRechazosYRelojAtrasado_BloqueadoDesdeElQuinto`, `Ingresar_RelojAtrasadoTrasElBloqueo_ContrasenaCorrectaBloqueada`, `Ingresar_RelojAtrasado_423ConContrasenaCorrecta` (src/Optica.Domain/Acceso/PoliticaBloqueo.cs:Evaluar; IngresarConContrasena.cs:EjecutarAsync)
- ✅ AC-02 — `PoliticaBloqueo_RelojAtrasado5Minutos_BloqueadoHastaAnclaMas5`
- ✅ AC-03 — `PoliticaBloqueo_RelojAtrasadoMasDe5Minutos_LiberadoSinError`
- ✅ AC-04 — `PoliticaBloqueo_RelojQueAvanza_BloqueadoHasta5MinutosYLuegoLibre`
- ✅ AC-05 — e2e `cinco intentos incorrectos bloquean el ingreso` (`e2e/tests/acceso.spec.ts:198`) en `chrome-acceso` y `msedge-acceso`: 68/68 en 5 corridas del cierre de CODE y 2 de esta verificación

FR-01, FR-02, FR-03 y NFR-01 de la RCA cubiertos por esas pruebas (NFR-01 fija el valor de la tolerancia en la prueba de
AC-02).

## Spec blocks
- ✅ Block 1 — el fix-plan (un solo grupo de pasos): pasos 1 a 9 implementados; el cambio en `src/` se limita a los 2 archivos previstos y ADR-006 no cambió

## Required tests (todas presentes y en verde)
- ✅ **Regression test** `PoliticaBloqueo_RelojAtrasado1Segundo_SigueBloqueado`
- ✅ `PoliticaBloqueo_RelojAtrasado5Minutos_BloqueadoHastaAnclaMas5`
- ✅ `PoliticaBloqueo_RelojAtrasadoMasDe5Minutos_LiberadoSinError`
- ✅ `PoliticaBloqueo_SeisRechazosYRelojAtrasado_BloqueadoDesdeElQuinto`
- ✅ `PoliticaBloqueo_RelojQueAvanza_BloqueadoHasta5MinutosYLuegoLibre`
- ✅ `PoliticaBloqueo_FechasNoUtcONulas_Rechazadas`
- ✅ **Regression test** `Ingresar_RelojAtrasadoTrasElBloqueo_ContrasenaCorrectaBloqueada`
- ✅ **Regression test** `Ingresar_RelojAtrasado_423ConContrasenaCorrecta`
- ✅ `QuintoRechazoConfirmadoJustoAntesDeLaConsulta_Bloqueado` y `CarreraInversa_QuintoConfirmadoPorOtroConHoraPosterior_Bloqueado` (Theory con desfases de 1 s y `ToleranciaRetroceso` + 1 s)
- ✅ e2e `cinco intentos incorrectos bloquean el ingreso`

Evidencia de regresión: con la condición anterior (`transcurrido >= TimeSpan.Zero`) fallan exactamente las 3 pruebas de
regresión, AC-02 y el caso de seis rechazos, por su aserción (el implementador lo mostró antes del arreglo y el
verificador lo repitió). Mutantes M1 a M7 (límite estricto, tolerancia + 1 tick, sin cota inferior, `<=`, fin
`ahora + Duracion`, hora antes de la consulta, sin segunda evaluación): todas mueren.

## Tests
- ✅ Sad-path tests: `PoliticaBloqueo_FechasNoUtcONulas_Rechazadas` rechaza fechas no UTC (incluida una hora local dentro de la tolerancia) con `ArgumentException` sin valores; `PoliticaBloqueo_RelojAtrasadoMasDe5Minutos_LiberadoSinError` cubre el retroceso mayor a la tolerancia sin excepción
- ✅ Dead code (W-VER-01): sin código muerto ni imports sin uso en los archivos del arreglo

## Warnings
- ⚠️ W-VER-03 — fuera de FIX-001 y previo: `ArticulosApiTests.Log_PostYPut_NoRegistraDescripcionCostoNiMargen` falló una vez en una corrida de mutantes: EF Core escribe la hora real en el SQL de `__EFMigrationsLock` y, cuando forma `54.32`, la prueba lo toma como el costo. No es una fuga (los parámetros de `Articulo` aparecen como `'?'`); no se repitió en 8 corridas de la suite ni en 25 aisladas. Para un ticket aparte.

## Notas
- AC-03 se prueba en el dominio (`Libre`); el caso de uso no hace nada propio con ese resultado y las pruebas existentes cubren que con `Libre` la contraseña correcta entra.
- Datos: el arreglo no agrega registros al log y el 423 no revela la hora de fin del bloqueo; la fecha de cada intento en `EventoAcceso` sigue siendo un dato personal de la operadora que no sale de la PC.

## Ronda 2 (tras el lazo correctivo)
La ronda 1 aprobó el código, pero `validate_verify.py` rechazó el informe por F-VER-02: el validador exige secciones
`## Block N` y la plantilla del fix-plan de un FIX no las tiene. Por decisión del usuario se volvió a PLAN, se agregó
`## Block 1` al fix-plan sin cambiar su contenido (0eb1207), se revalidaron el plan y el modelo de amenazas y se
re-cerró CODE sin cambios de código (`git diff 307fc07 HEAD -- src tests web e2e` vacío; .NET 1197/1197). El veredicto
de la ronda 1 sigue vigente. La suite de Api tardó 16 minutos en esa corrida (suele tardar menos de 1): pasó entera y no
se repitió la demora en el resto de la sesión; queda anotado.

Result: PASSED
