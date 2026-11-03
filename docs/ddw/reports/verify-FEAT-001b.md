# Verification FEAT-001b

## Ronda 1 — 2026-11-03 (HEAD `1ee61a4`)

| Field | Value |
|---|---|
| Module | Acceso: bloqueo por intentos, cambio y restablecimiento de la contraseña (`src/Optica.*/Acceso`, `Persistencia`, `Program.cs`, `web/src`, `e2e/tests/acceso.spec.ts`) |
| Line coverage | 95.8% |
| Branch coverage | 92.9% |
| Function coverage | 100% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes --no-restore` — clean; `dotnet build Optica.sln --no-incremental` — 0 warnings; `npm run build` en `web/` — clean |

Cobertura sobre el código nuevo o modificado: 23 archivos `.cs` de `src/` cambiados desde
`merge-base`, sin migraciones generadas, a partir de la unión de los 4 informes de coverlet (líneas
866/904, ramas 184/198, métodos 128/128). Sobre las líneas agregadas del diff: 95,0 % de líneas y
94,7 % de ramas. Los archivos más bajos son `Program.cs` (82,3/83,3) y `CandadoInstanciaArchivo.cs`
(84,4/85,7). Web: los archivos cambiados están al 100 %, salvo `cliente.ts` (100/97,9/100); el total de la web da 99,56/98,5/100.

Corridas de la verificación cruzada (`ddw-module-verifier`, en un clon fuera de `/mnt/c`): .NET
633/633, web 91/91, e2e unidad 25/25, harness 6 corridas con chrome 30/30 y msedge 30/30.

## Acceptance criteria
- ✅ AC-01 — `Ingresar_CincoRechazos_Bloqueado423ConLaCorrecta` (`PoliticaBloqueo.Evaluar`, `IngresarConContrasena.EjecutarAsync`, `AccesoController`), `Ingreso_Bloqueado423_MuestraMensajeDeBloqueo`, e2e `cinco intentos incorrectos bloquean el ingreso`
- ✅ AC-02 — `Ingresar_Pasados5Minutos_AceptaLaCorrecta`, `PoliticaBloqueo_A5MinutosDelQuintoRechazo_Desbloqueado`
- ✅ AC-03 — `ApiConSesion_NingunGet_DevuelveDatosArca`, `Pantallas_NingunaMuestraDatosArca`
- ✅ AC-04 — `CambiarContrasena_ActualCorrecta_AceptaLaNuevaYRechazaLaAnterior` (`CambiarContrasena.EjecutarAsync`, `AccesoController.Cambiar`), `CambiarContrasena_Correcta_MuestraExitoEnInicio`, e2e `cambia la contraseña e ingresa con la nueva`
- ✅ AC-05 — `Cambiar_ActualIncorrecta_Invalido400ConCampoActual`, `CambiarContrasena_ActualIncorrecta_RechazadaConMensajeJuntoAlCampo`
- ✅ AC-06 — `Comando_ConfirmacionCorrecta_SiguienteIngresoPideDefinir` (`RestablecerContrasena.EjecutarAsync`, `ComandoRestablecerContrasena`), `RestablecerContrasena_EliminaSoloLaCredencialYRegistraEvento`
- ✅ AC-07 — `Restablecimiento_NoExisteEnLaApi_NingunEndpointLoOfrece`

## Spec blocks
- ✅ Block 1 — tareas completas; 10/10: `PoliticaBloqueo_CincoRechazosConsecutivos_Bloqueado5Minutos`, `PoliticaBloqueo_SeisRechazosPorConcurrencia_Bloqueado`, `PoliticaBloqueo_A5MinutosDelQuintoRechazo_Desbloqueado`, `PoliticaBloqueo_ExitoReiniciaLaCuenta`, `PoliticaBloqueo_DecimoRechazo_BloqueadoOtraVez`, `PoliticaBloqueo_RelojAtrasado_SinBloqueoNiError`, `PoliticaBloqueo_RelojAtrasado_DecimoRechazoBloqueaConLaHoraNueva`, `RegistroEventos_TextoDesconocido_IgnoradoSinError`, `RegistroEventos_RechazosDesdeUltimoReinicio_OrdenadosPorId`, `Migracion_IndiceTipoResultado_CreadoYRevertible`
- ✅ Block 2 — tareas completas; 8/8: `IngresarConContrasena_Bloqueado_RechazadoSinCalcularHashYEventoRegistrado`, `IngresarConContrasena_QuintoRechazo_DevuelveBloqueado`, `IngresarConContrasena_FueraDePoliticaDuranteBloqueo_Invalido400SinEvento`, `Ingresar_CincoRechazos_Bloqueado423ConLaCorrecta`, `Ingresar_Pasados5Minutos_AceptaLaCorrecta`, `Ingresar_DosPedidosSimultaneosEnElCuarto_Bloqueado423`, `Ingresar_BloqueoSobreviveAlReinicio_Bloqueado423`, `Ingresar_ResultadoBloqueado_NoEmiteCookie`
- ✅ Block 3 — tareas completas; 5/5: `CredencialAcceso_ReemplazarHash_ActualizaHashYFecha`, `CredencialAcceso_ReemplazarConFechaNoUtc_Rechazado`, `Reemplazar_CredencialCambiadaEnElMedio_Conflicto`, `ValidacionContrasena_CampoComoParametro_ErrorEnEseCampo`, `Modelo_SinCambiosPendientes_TrasLasMigraciones`
- ✅ Block 4 — tareas completas; 11/11: `CambiarContrasena_ActualCorrecta_AceptaLaNuevaYRechazaLaAnterior`, `CambiarContrasena_ActualIncorrecta_RechazadaSinCambiarYEventoRegistrado`, `CambiarContrasena_ActualIncorrecta_NoCuentaParaElBloqueo`, `CambiarContrasena_NuevaDe7Caracteres_InvalidoSinEvento`, `CambiarContrasena_SinCredencial_Conflicto`, `Cambiar_ActualIncorrecta_Invalido400ConCampoActual`, `Cambiar_ActualFaltante_Invalido400`, `Cambiar_NuevaInvalida_Invalido400ConCampoNueva`, `Cambiar_SinSesion_NoAutorizado401`, `Cambiar_DosCambiosSimultaneos_Conflicto409`, `Log_Cambiar_NoContieneNingunaContrasena`
- ✅ Block 5 — tareas completas; 15/15: `RestablecerContrasena_EliminaSoloLaCredencialYRegistraEvento`, `RestablecerContrasena_SinCredencial_RegistraEventoYRenuevaClaves`, `RestablecerContrasena_ErrorAlConfirmar_NoEliminaNada`, `RestablecerContrasena_FallaAlRenovarClaves_Error`, `Comando_ConfirmacionCorrecta_SiguienteIngresoPideDefinir`, `Comando_ConfirmacionInvalida_CanceladoSinCambios`, `Comando_ApiEnMarcha_ErrorSinCambios`, `Comando_BaseInexistente_ErrorSinCrearla`, `Comando_MigracionesPendientes_ErrorSinMigrar`, `Comando_ErrorDeSqlite_ErrorSinRutaNiExcepcion`, `Comando_RenuevaLasClaves_SesionAnteriorNoAutorizada`, `Restablecimiento_NoExisteEnLaApi_NingunEndpointLoOfrece`, `CandadoInstancia_SegundaInstancia_RechazadaHastaQueLaPrimeraLoLibere`, `Api_SegundaInstanciaSobreElMismoDirectorio_RechazadaAlArrancar`, `FabricaApi_PruebasEnParalelo_ArrancanCadaUnaConSuCandado`
- ✅ Block 6 — tareas completas; 8/8: `Ingreso_Bloqueado423_MuestraMensajeDeBloqueo`, `CambiarContrasena_Correcta_MuestraExitoEnInicio`, `CambiarContrasena_ActualIncorrecta_RechazadaConMensajeJuntoAlCampo`, `CambiarContrasena_Nueva7Caracteres_InvalidaSinEnviar`, `CambiarContrasena_SinSesionAlAbrir_NoAutorizadoNavegaAIngreso`, `CambiarContrasena_409_ConflictoMuestraMensaje`, `CambiarContrasena_ErrorDeRed_MuestraMensajeGeneral`, `CambiarContrasena_NingunaContrasenaQuedaEnElNavegador`
- ✅ Block 7 — tareas completas; 4/4: `ApiConSesion_NingunGet_DevuelveDatosArca`, `Pantallas_NingunaMuestraDatosArca`, `acceso.spec.ts: cambia la contraseña e ingresa con la nueva`, `acceso.spec.ts: cinco intentos incorrectos bloquean el ingreso`

Las 61 pruebas requeridas por la spec existen con su nombre exacto y pasan.

## Tests
- ✅ Sad-path tests: `Cambiar_ActualFaltante_Invalido400`, `Cambiar_NuevaInvalida_Invalido400ConCampoNueva`, `Cambiar_SinSesion_NoAutorizado401`, `IngresarConContrasena_FueraDePoliticaDuranteBloqueo_Invalido400SinEvento`, `Comando_ConfirmacionInvalida_CanceladoSinCambios`, `Comando_BaseInexistente_ErrorSinCrearla`, `Comando_MigracionesPendientes_ErrorSinMigrar`, `Comando_ErrorDeSqlite_ErrorSinRutaNiExcepcion`, `CredencialAcceso_ReemplazarConFechaNoUtc_Rechazado`, `CambiarContrasena_Nueva7Caracteres_InvalidaSinEnviar`: cada endpoint, comando y función con entrada tiene al menos un caso inválido

## Warnings (no bloquean)
1. W-VER-01: `CredencialAcceso.ReemplazarHash` y `PoliticaBloqueo.RechazosDesdeUltimoReinicio` solo los usan las pruebas.
2. Datos sensibles: `ConflictoAlConfirmarException(string mensaje, Exception? causa = null)` (`src/Optica.Application/Comun/IUnidadDeTrabajo.cs:31`) acepta una causa. Si alguien adjunta la `DbUpdateConcurrencyException`, el hash de la contraseña podría llegar al log. **El usuario decidió corregirlo ahora: loop correctivo a CODE.**
3. El comentario de `IngresarConContrasena.cs:87-88` atribuye la segunda evaluación a "si el reloj no avanzó"; la causa real es que la hora de la PC retroceda.
4. Ramas de error sin prueba: el `catch (Exception)` de `Program.EjecutarComandoAsync`, la lambda real del host del comando y, en `CandadoTomado.Tomar`, una falla del candado que no sea de uso compartido.
5. AC-07: ninguna prueba de la web fija que ninguna pantalla ofrezca el restablecimiento.
6. AC-03: la Api se revisa solo por nombres de clave (contradicción interna de la spec, aceptada), y la prueba ejecuta todos los GET registrados.
7. AC-06: "los demás datos" quedan fijados solo para las dos tablas que hay hoy.
8. W-VER-03: e2e dependientes del orden, enumeración de endpoints duplicada; msedge 0 fallas en 6 corridas.
9. Deuda del Block 6: el 409 por concurrencia muestra "No hay una contraseña definida", y el aviso de éxito reaparece desde `history.state`.

## Decisiones del usuario en esta ronda
- P-1, prueba del candado en Windows nativo (`Comando_ApiEnMarcha_ErrorSinCambios` y `Api_SegundaInstanciaSobreElMismoDirectorio_RechazadaAlArrancar`): **riesgo aceptado sin verificar.**
- P-3, NFR-02 con la versión inmediata anterior de Chrome y Edge (155; solo se probó la 156): **riesgo aceptado sin verificar.**
- P-2, intermitencia de msedge W-VER-03 (2/11 en CODE, 0/6 en VERIFY): **aceptada para FEAT-001b.**

Result: PASSED — sin FAIL, pero el usuario decidió volver a CODE para corregir el WARN 2 antes de cerrar.
