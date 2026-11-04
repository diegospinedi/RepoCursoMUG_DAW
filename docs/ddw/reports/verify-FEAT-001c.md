# Verification FEAT-001c

Ronda 1, sobre el commit `23ed81f` de la rama `feat/FEAT-001c-parametros-negocio`. Verificación cruzada de
ddw-module-verifier (agente que no escribió el código), que volvió a correr `dotnet test Optica.sln`
(756/756), `npm run test:coverage` en `web/` (174/174), `npm run test:unidad` en `e2e/` (49/49), `npm run
build` y `dotnet format --verify-no-changes`. El arnés e2e lo corrió el orquestador: 24/24 en Chrome y Edge.

| Field | Value |
|---|---|
| Module | configuración de parámetros de negocio: `src/Optica.Domain/Configuracion/`, `src/Optica.Application/Configuracion/`, `src/Optica.Infrastructure/Persistencia/ConfiguracionNegocioRepository.cs`, `src/Optica.Api/Controllers/ConfiguracionController.cs`, `web/src/paginas/Configuracion.tsx`, `web/src/configuracion/` |
| Line coverage | 93.7% |
| Branch coverage | 86.0% |
| Function coverage | 97.3% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean; `dotnet build Optica.sln --no-incremental` — 0 warnings; `npm run build` en `web/` (tsc con noUnusedLocals y noUnusedParameters) — clean |

Cobertura de la tabla: código nuevo o modificado de .NET (los 13 archivos de `src/` que cambiaron en la
rama, sin migraciones): líneas 534/570, ramas 117/136, funciones 73/75. Web, por separado: líneas 99.26%,
ramas 97.23%, funciones 100%. Total del proyecto (informe de pruebas): .NET 94.5% / 92.2% / 93.7%.

## Acceptance criteria
- ✅ AC-01 — `GrabarConfiguracionNegocio.EjecutarAsync`, `ConfiguracionNegocioRepository.ObtenerAsync` (lee en cada llamada); pruebas `Reemplazar_YConfirmar_OtroContextoLeeElValorNuevo`, `Grabar_TopeNuevo_ObtenerDevuelveElNuevo`, `Post_TopeNuevo_GetPosteriorLoDevuelve`, `Configuracion_GrabaTope_MuestraValorGuardado`, e2e `configuracion: el tope grabado se ve al volver a abrir y sin reiniciar`
- ✅ AC-02 — `PoliticaConfiguracionNegocio.LeerCondicion`/`TextoDe`, selector de `Configuracion.tsx`; pruebas `Grabar_Monotributo_ObtenerDevuelveMonotributo`, `Post_Monotributo_GetPosteriorLoDevuelve`, `Configuracion_GrabaMonotributo_MuestraMonotributo`, e2e `configuracion: Monotributo queda vigente al volver a abrir`
- ✅ AC-03 — `PoliticaConfiguracionNegocio` (rango de la alícuota), `reglas.ts`, `leerDecimal` con signo menos (`caf4657`); pruebas `Evaluar_Alicuota101_Rechazado`, `Evaluar_AlicuotaMenos1_Rechazado`, `Grabar_Alicuota101_RechazadoSinGrabar`, `Post_Alicuota101_Rechazado400EnElCampo`, `Configuracion_Alicuota101_RechazadoJuntoAlCampo`, `Configuracion_AlicuotaMenos1_RechazadoJuntoAlCampo`, e2e `configuracion: alícuota 101 y -1 rechazado junto al campo`
- ✅ AC-04 — mínimo del múltiplo antes que la escala (D-01); pruebas `Evaluar_Multiplo0coma001_RechazadoPorMinimo`, `Grabar_Multiplo0coma001_RechazadoConMinimo`, `Post_Multiplo0coma001_Rechazado400EnElCampo`, `Configuracion_Multiplo0coma001_RechazadoJuntoAlCampo`, e2e `configuracion: múltiplo 0,001 rechazado con el mínimo`
- ✅ AC-05 — tope no positivo; pruebas `Evaluar_Tope0_Rechazado`, `Grabar_Tope0_RechazadoMayorACero`, `Post_Tope0_Rechazado400EnElCampo`, `Configuracion_Tope0_RechazadoJuntoAlCampo`, e2e `configuracion: tope 0 rechazado junto al campo`
- ✅ AC-06 — migración `20261104134158_ConfiguracionNegocio` (`InsertData` 2100, 'responsable_inscripto', 1, NULL); pruebas `BaseNueva_TieneFilaConValoresIniciales`, `BaseDeFeat001b_AlMigrar_RecibeLaFila`, `Get_ConSesion_DevuelveValoresIniciales`, `Configuracion_PrimeraVez_MuestraValoresIniciales`, e2e `configuracion: primera vez muestra 21, Responsable Inscripto y 0,01` en Chrome y Edge
- ✅ AC-07 — `ConfiguracionNegocio.FaltaTope` y el aviso con `role="status"`; pruebas `FaltaTope_SinTope_Verdadero`, `Grabar_TopeAusente_GrabaSinTope`, `Configuracion_SinTope_MuestraAviso`, e2e `configuracion: sin tope muestra el aviso`

## Spec blocks
- ✅ Block 1 — dominio: 17/17 pruebas requeridas presentes y en verde
- ✅ Block 2 — infraestructura: 10/10 presentes, incluida `Migracion_IndiceTipoResultado_CreadoYRevertible` actualizada
- ✅ Block 3 — aplicación: 11/11 presentes
- ✅ Block 4 — Api: 13/13 presentes; `EndpointsProtegidos_SinSesion_NoAutorizado401` y `RutasAnonimas_SonExactamenteCinco` siguen en verde sin cambios
- ✅ Block 5 — web: 16/16 presentes; las pruebas del 413 renombradas por D-11 son adicionales a las requeridas
- ✅ Block 6 — e2e: 9/9 presentes, incluidas `instancias: nombre mal formado rechazado` y `servidor: una Api que no arranca da error`

## Tests
- ✅ Sad-path tests: cada superficie de entrada tiene casos inválidos — `Post_VariosInvalidos_400ConTodosLosCampos`, `GetYPost_SinSesion_NoAutorizado401`, `Post_SinContentTypeJson_Rechazado415`, `Post_CuerpoGrande_Rechazado413`, `Post_CuerpoIlegible_Invalido400SinDetallesInternos` (9 cuerpos), `Get_SinFila_Error500Generico`, `Insertar_AlicuotaFueraDeRango_RechazadoPorCheck`, `Insertar_CondicionDesconocida_RechazadoPorCheck`, `Constructor_CondicionNoDefinida_Rechazado`, `leerDecimal_Texto_Invalido`, `Configuracion_NoNumero_Invalido`, `Configuracion_SinSesion401_NavegaAlIngreso`
- ✅ Dead code (W-VER-01): sin imports sin uso (tsc con noUnusedLocals); `campo413` sin uso eliminado en la ronda 2 de Block 5
- ⚠️ Fragile tests (W-VER-03): `e2e/tests/configuracion.spec.ts` corre en serie y cada prueba depende de lo que grabó la anterior (intencional, D-05: si una falla, las siguientes también); persiste la intermitencia conocida de msedge en `acceso.spec.ts` (1 falla en 5 corridas durante Block 6, aceptada en FEAT-001a y FEAT-001b; este ticket no toca ese archivo)

## Tests promised by the spec
Las 76 pruebas que nombran los Required tests de la spec existen en el repositorio (comprobado con `git grep`) y pasan en la corrida de esta ronda:
- ✅ Block 1: `Evaluar_ValoresIniciales_SinInfracciones`, `Evaluar_Alicuota101_Rechazado`, `Evaluar_AlicuotaMenos1_Rechazado`, `Evaluar_AlicuotaBordes0y100y10coma5_Validos`, `Evaluar_Alicuota21coma555_Invalido`, `Evaluar_Multiplo0coma001_RechazadoPorMinimo`, `Evaluar_Multiplo0coma015_Invalido`, `Evaluar_Tope0_Rechazado`, `Evaluar_TopeNegativo_Rechazado`, `Evaluar_TopeAusente_Valido`, `Evaluar_CondicionDesconocida_Invalido`, `Evaluar_ImporteSobreElMaximo_Rechazado`, `Evaluar_VariosCamposInvalidos_DevuelveUnaInfraccionPorCampo`, `Constructor_ValorInvalido_LanzaSinIncluirElValor`, `FaltaTope_SinTope_Verdadero`, `Evaluar_CerosALaDerecha_Validos`, `Constructor_CondicionNoDefinida_Rechazado`
- ✅ Block 2: `BaseNueva_TieneFilaConValoresIniciales`, `BaseDeFeat001b_AlMigrar_RecibeLaFila`, `Reemplazar_YConfirmar_OtroContextoLeeElValorNuevo`, `Reemplazar_TopeNull_GuardaNull`, `Insertar_AlicuotaFueraDeRango_RechazadoPorCheck`, `Insertar_CondicionDesconocida_RechazadoPorCheck`, `Insertar_SegundaFila_RechazadoPorCheck`, `Obtener_SinFila_LanzaError`, `AgregarInfraestructura_RegistraRepositorioDeConfiguracion`, `Migracion_IndiceTipoResultado_CreadoYRevertible`
- ✅ Block 3: `Grabar_TopeNuevo_ObtenerDevuelveElNuevo`, `Grabar_Monotributo_ObtenerDevuelveMonotributo`, `Grabar_Alicuota101_RechazadoSinGrabar`, `Grabar_Multiplo0coma001_RechazadoConMinimo`, `Grabar_Tope0_RechazadoMayorACero`, `Grabar_VariosCamposInvalidos_DevuelveTodosLosErrores`, `Grabar_TopeAusente_GrabaSinTope`, `Grabar_Exitoso_RegistraValoresAnterioresYNuevos`, `Grabar_Invalido_NoRegistraNiConfirma`, `Obtener_SinFila_PropagaError`, `Grabar_ConflictoAlConfirmar_SePropaga`
- ✅ Block 4: `Get_ConSesion_DevuelveValoresIniciales`, `Post_TopeNuevo_GetPosteriorLoDevuelve`, `Post_Monotributo_GetPosteriorLoDevuelve`, `Post_Alicuota101_Rechazado400EnElCampo`, `Post_Multiplo0coma001_Rechazado400EnElCampo`, `Post_Tope0_Rechazado400EnElCampo`, `Post_VariosInvalidos_400ConTodosLosCampos`, `GetYPost_SinSesion_NoAutorizado401`, `Post_SinContentTypeJson_Rechazado415`, `Post_CuerpoGrande_Rechazado413`, `Post_CuerpoIlegible_Invalido400SinDetallesInternos`, `Get_SinFila_Error500Generico`, `Appsettings_GrabacionDeConfiguracion_VaAlRegistroDeEventos`
- ✅ Block 5: `leerDecimal_ComaYPunto_Validos`, `leerDecimal_Texto_Invalido`, `reglas_ImporteSobreElMaximoSeguro_Rechazado`, `Configuracion_PrimeraVez_MuestraValoresIniciales`, `Configuracion_SinTope_MuestraAviso`, `Configuracion_GrabaTope_MuestraValorGuardado`, `Configuracion_GrabaMonotributo_MuestraMonotributo`, `Configuracion_Alicuota101_RechazadoJuntoAlCampo`, `Configuracion_Multiplo0coma001_RechazadoJuntoAlCampo`, `Configuracion_Tope0_RechazadoJuntoAlCampo`, `Configuracion_NoNumero_Invalido`, `Configuracion_Error400DelServidor_MuestraCadaCampo`, `Configuracion_SinSesion401_NavegaAlIngreso`, `Configuracion_SinConexion_ErrorGeneralConservaValores`, `grabarConfiguracion_400_DevuelveTodosLosErrores`, `Inicio_EnlaceConfiguracion`
- ✅ Block 6: `configuracion: primera vez muestra 21, Responsable Inscripto y 0,01`, `configuracion: sin tope muestra el aviso`, `configuracion: alícuota 101 y -1 rechazado junto al campo`, `configuracion: múltiplo 0,001 rechazado con el mínimo`, `configuracion: tope 0 rechazado junto al campo`, `configuracion: Monotributo queda vigente al volver a abrir`, `configuracion: el tope grabado se ve al volver a abrir y sin reiniciar`, `instancias: nombre mal formado rechazado`, `servidor: una Api que no arranca da error`

## Threat model mitigations
- ✅ R-01 a R-08 presentes y probados (sesión, 415, tres barreras de validación más el conversor que no trunca, registro de grabaciones con EventLog, 413, sin `dangerouslySetInnerHTML` y control de CSP en el e2e, textos de error fijos, relectura después de grabar)

## Warnings (no bloquean)
- W1 — W-VER-03, descrito arriba.
- W2 — AC-03 con -1 se prueba en dominio, web y e2e, no en aplicación ni en Api (mismo camino, `PoliticaConfiguracionNegocio`).
- W3 — `servidor: una Api que no arranca da error` prueba `esperarApi`, no la salida con código distinto de 0 de `servidor.mjs`.
- W4 — la sección "Files" de Block 5 en la spec todavía dice `campo413 alicuotaIva`; el código sigue D-11 (413 como mensaje general), trazado en `decisions-FEAT-001c.md`.
- W5 — en la web, un 200 con cuerpo ilegible o un 5xx muestran "No se pudo conectar con el sistema. Reintentá.", siguiendo el patrón previo de `cliente.ts`.
- W6 — riesgo de carga aceptado por el usuario (D-09): "350.000" en el tope se graba como 350 y "1.500" en el múltiplo como 1,5.
- NFR-01: la versión estable inmediata anterior de Chrome y de Edge se verifica a mano, como en FEAT-001a y FEAT-001b; las pruebas automáticas corren en la versión estable instalada.

Datos personales: sin riesgo de exposición; los cuatro parámetros son datos de negocio y el registro de grabaciones lleva solo esos valores y la fecha.

Result: PASSED
