# Verification FEAT-008

Recuperación de emisiones sin respuesta de ARCA. Ronda 1 de VERIFY, sobre el commit del cierre de CODE (rama
`feat/FEAT-008-recuperacion-emisiones`, base `6727d98`). Verificación cruzada de ddw-module-verifier (agente que no
escribió el código) contra `docs/ddw/prd/prd-FEAT-008.md`, `docs/ddw/specs/spec-FEAT-008.md`,
`docs/ddw/specs/decisions-FEAT-008.md` y la corrida `docs/ddw/reports/tests-FEAT-008.md` (3799/3799). El verificador
volvió a correr pruebas filtradas: Application 39/39, Api 32/32, Vitest de `Inicio` y `Presupuesto` 119/119.

| Field | Value |
|---|---|
| Module | `src/Optica.Application/Facturacion` (VerificarEmision, DescartarEmision, ProcesoEmision, ListarEmisionesPendientes), `src/Optica.Infrastructure/Arca` y `Persistencia`, `src/Optica.Api/Controllers`, `web/src/paginas/Presupuesto.tsx`, `web/src/paginas/Inicio.tsx`, `web/src/api/cliente.ts`, `e2e/tests/verificacion-*` |
| Line coverage | 95.0% |
| Branch coverage | 86.6% |
| Function coverage | 97.8% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean; `dotnet build` — 0 warnings; `npm run lint` (oxlint) — clean; `npm run build` (tsc) — clean; `dotnet format style` IDE0005/IDE0051/IDE0052/IDE0060 sin hallazgos nuevos |

Cobertura de la tabla: los 36 archivos .NET de producción del ticket (nuevos o modificados, sin migraciones), unión de
los cuatro informes de coverlet del cierre de CODE: líneas 3529/3714, ramas 1119/1292, funciones 483/494. Web, archivos
del ticket: `cliente.ts` 94,89 % líneas, 86,07 % ramas, 96,96 % funciones; `Presupuesto.tsx` 95,86 %, 92,23 %, 100 %.
Suite completa: .NET 96,9 / 90,1 / 97,3; web 98,82 / 96,69 / 100.

## Acceptance criteria
- ✅ AC-01 — `ProcesoEmisionTests.Emitir_RegistraNumero125AntesDeEnviar` (`ProcesoEmision.cs:102`, `:156`, `:122`); `AutorizadorWsfeTests.cs:527`, `:541`; `EmisionPendienteRepositoryTests.cs:131`
- ✅ AC-02 — `Presupuesto.test.tsx:1276`; `VerificacionApiTests.Get_ConObservacion_SinDni`; e2e `verificacion-recupera.spec.ts:162`
- ✅ AC-03 — `ListarEmisionesPendientesTests.Listar_DosPendientes_155y160`; `VerificacionApiTests.cs:347`; `Inicio.test.tsx:189`; e2e recupera
- ✅ AC-04 — `ListarEmisionesPendientesTests.cs:30`; `VerificacionApiTests.cs:347` (lista vacía); `Inicio.test.tsx:231`; e2e recupera `:155`
- ✅ AC-05 — `VerificarEmisionTests.cs:68` (`VerificarEmision.cs:146`, `Factura.Recuperada`); `VerificacionApiTests.cs:42`; `SimuladorArcaTests.cs:368`, `:421`; e2e recupera
- ✅ AC-06 — `VerificarEmisionTests.cs:112` (orden consulta → próximo → autorizar); `AutorizadorWsfeTests.cs:587`; e2e `verificacion-emite.spec.ts:100`
- ✅ AC-07 — `VerificarEmisionTests.cs:158`; `VerificacionApiTests.cs:80` (409 y relectura por GET); `Presupuesto.test.tsx:1292`; e2e importe-distinto
- ✅ AC-08 — `DescartarEmisionTests.cs:42`; `VerificacionApiTests.cs:212` (204, sin pendiente, 1 descarte); `ObservacionDescarteRepositoryTests.cs:187`; `Presupuesto.test.tsx:1330`
- ✅ AC-09 — `Presupuesto.test.tsx:1359` (cancelar: 0 POST de descarte); e2e importe-distinto
- ✅ AC-10 — `DescartarEmisionTests.cs:79`, `:101`; `VerificacionApiTests.cs:190` (409 `sin_observacion`, pendiente intacta)
- ✅ AC-11 — `VerificarEmisionTests.cs:189` (TimeProvider: 29 s sin terminar, 30 s sin respuesta, 0 envíos); `AutorizadorWsfeTests.cs:725`; `Presupuesto.test.tsx:1382`; e2e `verificacion-consulta-sin-respuesta.spec.ts:99`
- ✅ AC-12 — `VerificarEmisionTests.cs:250` (pendiente con el número 126); `ProcesoEmisionTests.cs:168`; e2e `verificacion-sin-respuesta.spec.ts:105` (número nuevo indirecto, D-36)
- ⚠️ AC-13 — contra el simulador: `VerificarEmisionTests.cs:526`, `VerificacionApiTests.cs:409` (un solo comprobante y una sola factura); contra ARCA homologación real queda pendiente de verificación manual con el certificado (D-01), antes de facturar en producción
- ✅ AC-14 — `e2e/playwright.config.ts:44-45`: los 5 archivos `verificacion-*` en Chrome y Edge estables (170 e2e, 85 por navegador)
- ✅ AC-15 — `VerificarEmisionTests.cs:278`; `VerificacionApiTests.cs:117` (fila de FEAT-007 por SQL, D-06); `Presupuesto.test.tsx:1400`
- ✅ AC-16 — `VerificarEmisionTests.cs:305`; `VerificacionApiTests.cs:146`; `Presupuesto.test.tsx:1421`; `DescartarEmisionTests.cs:101`
- ✅ AC-17 — `VerificarEmisionTests.cs:337` (40 s, redondeo, a los 60 s consulta); `VerificacionApiTests.cs:168`; `Presupuesto.test.tsx:1436`

Ninguna prueba de la Api se queda en el código de estado: todas releen el estado (GET, tabla o archivo del simulador).

## Spec blocks
- ✅ Block 1 — 14/14 tareas, 11/11 pruebas con el nombre de la spec; migración `20261114120000_RecuperacionEmisiones` (errata del nombre en la spec, D-23)
- ✅ Block 2 — 9/9 tareas, 10/10 pruebas (`AutorizadorWsfeTests.cs:527-765`)
- ✅ Block 3 — 3/3 tareas, 10/10 pruebas (`SimuladorArcaTests.cs:368-657`, `OpcionesArcaTests.cs:214`)
- ✅ Block 4 — 3/3 tareas, 6/6 pruebas (`ProcesoEmisionTests.cs`, `EmitirFacturaTests.cs:690`, `:734`)
- ✅ Block 5 — 7/7 tareas (más `OpcionesVerificacion.cs` y `PlazoEmision.cs`, D-30), 16/16 pruebas
- ⚠️ Block 6 — 4/4 tareas, 13/13 pruebas cubiertas; `Verificacion_ResultadoNoPrevisto_Error500` está como `Verificacion_AutorizadorLanza_Error500` (`VerificacionApiTests.cs:322`) más `CierrePorDefectoPresupuestosTests.ResponderVerificacion_ResultadosPrevistos_SuCodigo` (`:465`)
- ✅ Block 7 — 4/4 tareas, 12/12 pruebas (`Presupuesto.test.tsx:1276-1450`, `Inicio.test.tsx:189`, `:231`, `:245`)
- ✅ Block 8 — 6/6 tareas, 6/6 pruebas; `verificacion-sin-respuesta.spec.ts` dividido en `verificacion-consulta-sin-respuesta.spec.ts` (AC-11) y `verificacion-sin-respuesta.spec.ts` (AC-12), D-36

## Tests
- ✅ Sad-path tests: verificación con 401 (`RutasNuevas_SinSesion_401`), 404 (`Verificacion_PresupuestoInexistente_404`), 413 (`Verificacion_CuerpoGrande_413`), 500 (`Verificacion_AutorizadorLanza_Error500`) y un 409 por motivo; descarte con 401, 404, 413 y 409 (`Descarte_DirectoSinVerificar_409SinObservacion`); lista de pendientes con 401; casos de uso con inexistente, sin pendiente, no grabable, espera, sin número, otro entorno y timeout
- ✅ Spec tests: todas las pruebas listadas en la spec existen y pasan (una renombrada, ver Block 6)
- ✅ Lint / type checker: limpio (tabla de arriba)
- ✅ W-VER-01: sin código muerto ni imports sin usar en el diff de producción
- ⚠️ W-VER-02: `src/Optica.Application/Facturacion/VerificarEmision.cs` (lógica central) 202/236 líneas (85,6 %) y 52/77 ramas (67,5 %); otros archivos del ticket bajo el 80 % de ramas: `ResultadoConsulta.cs` 5/10, `ResultadoProximoNumero.cs` 3/6, `ResultadoVerificacion.cs` 22/32 (guardas de fábricas), `AutorizadorWsfe.cs` 18/24, `AutorizadorNoConfigurado.cs` 2/4, `DescarteEmisionRepository.cs` 5/10, `TextosFacturacion.cs` 28/36; recomendable llevar `VerificarEmision.cs` a 90 % en un ticket posterior
- ⚠️ W-VER-03: `e2e/tests/verificacion-consulta-sin-respuesta.spec.ts:141` (`demora >= 1_900`) y `:151` (`waitForTimeout(2_500)`) dependen del reloj real
- ⚠️ W-VER-03: las esperas previas a verificar en los 5 `verificacion-*` se calculan con `Date.now()` del navegador y no con el momento de la pendiente en el servidor (margen de D-31: 8 s y 6 s contra 3 s)
- ⚠️ W-VER-03: `e2e/tests/verificacion-recupera.spec.ts:22` corre en modo serial y AC-04 (`:155`) depende de correr antes que `:162`

## Required tests (spec)

Block 1:
- ✅ `EmisionPendiente_ConSolicitudYNumero_GuardaNumeroConceptoEImportes`
- ✅ `EmisionPendiente_NumeroInvalido_Rechazado`
- ✅ `ObservacionEmision_ImporteDistintoSinTotalArca_Rechazado`
- ✅ `Repositorio_FilaParcialmenteNula_ErrorDeLectura`
- ✅ `Repositorio_GrabaYLeePendienteConNumero_SinDni`
- ✅ `Repositorio_FilaDeFeat007_SeLeeSinNumero`
- ✅ `Repositorio_ListarNumeros_OrdenAscendente`
- ✅ `Descarte_UpdateOBorrado_RechazadoPorTrigger`
- ✅ `Observacion_Update_RechazadoPorTrigger`
- ✅ `Migracion_SobreBaseConPendientesDeFeat007_ConservaTriggerYColumnasNulas`
- ✅ `Migracion_Down_Revierte`

Block 2:
- ✅ `Consultar_Autorizado_DevuelveTotalCaeVencimientoYFecha`
- ✅ `Consultar_Error602_NoAutorizado`
- ✅ `Consultar_TokenSinPlazo_NoConsultadaSinRed`
- ✅ `Consultar_FallaWsaa_NoConsultada`
- ✅ `Consultar_RespuestaIlegible_SinRespuesta`
- ✅ `Consultar_Timeout_SinRespuesta`
- ✅ `Consultar_OtroError_SinRespuesta`
- ✅ `Consultar_RespuestaConDni_NiLogNiResultadoLoLlevan`
- ✅ `ProximoNumero_UltimoMasUno`
- ✅ `Autorizar_UsaElNumeroRecibido`

Block 3:
- ✅ `Simulador_AutorizaYConsulta_MismoTotal`
- ✅ `Simulador_ConsultaNumeroNoAutorizado_NoAutorizado`
- ✅ `Simulador_AutorizarSinResponder_RegistraYSinRespuesta`
- ✅ `Simulador_NoResponderUnaVez_SegundaAutoriza`
- ✅ `Simulador_ConsultaNoResponder_SinRespuesta`
- ✅ `Simulador_NumeroIncorrecto_Rechazado`
- ✅ `Simulador_ArchivoInvalido_NoConsultada`
- ✅ `Opciones_ValorDesconocido_ErrorDeConfiguracion`
- ✅ `Simulador_CrecimientoMasDe50_ConservaUltimos50YNoSuperaLimite`
- ✅ `Simulador_ArchivoDeFeat007_SeLee`

Block 4:
- ✅ `Emitir_RegistraNumero125AntesDeEnviar`
- ✅ `Emitir_ProximoNumeroFalla_NoEnviadaSinPendiente`
- ✅ `Emitir_FacturaGrabadaPorOtroAntesDeLaPendiente_ConflictoSinEnviar`
- ✅ `Emitir_MargenInsuficiente_NoEnviadaYQuitaPendiente`
- ✅ `Emitir_SinRespuesta_PendienteConNumero`
- ✅ `Emitir_DosPedidosEnParalelo_UnSoloComprobante`

Block 5:
- ✅ `Verificar_AutorizadoMismoTotal_RecuperaSinEnviarSolicitud`
- ✅ `Verificar_NoAutorizado_ConsultaPrimeroYEmiteConNumeroNuevo`
- ✅ `Verificar_ImporteDistinto_ObservacionSinEmitir`
- ✅ `Verificar_ConsultaSinRespuesta30s_PendienteIntactaSinReintento`
- ✅ `Verificar_EmisionSinRespuesta_PendienteConNumeroNuevo`
- ✅ `Verificar_SinNumero_ObservacionSinConsultar`
- ✅ `Verificar_OtroEntorno_ObservacionSinConsultar`
- ✅ `Verificar_A20Segundos_EsperaFaltan40SinConsultar`
- ✅ `Verificar_SinPendiente_Conflicto`
- ✅ `Verificar_RecuperadaNoGrabable_PendienteIntacta`
- ✅ `Descartar_ConObservacion_RegistraYQuitaPendiente`
- ✅ `Descartar_SinObservacion_Rechazado`
- ✅ `Descartar_OtroEntornoQueYaCoincide_Rechazado`
- ✅ `Verificar_EscenariosAC05aAC12_UnComprobantePorPresupuesto`
- ✅ `Listar_DosPendientes_155y160`
- ✅ `Listar_SinPendientes_Vacia`

Block 6:
- ✅ `Verificacion_Recuperada_200ConFactura`
- ✅ `Verificacion_ImporteDistinto_409ConImportes`
- ✅ `Verificacion_PendienteSinNumero_409SinNumero`
- ✅ `Verificacion_PendienteOtroEntorno_409OtroEntorno`
- ✅ `Verificacion_Antes60s_409Espera`
- ✅ `Descarte_DirectoSinVerificar_409SinObservacion`
- ✅ `Descarte_TrasObservacion_204YGetSinPendiente`
- ✅ `RutasNuevas_SinSesion_401`
- ✅ `Verificacion_PresupuestoInexistente_404`
- ✅ `Verificacion_CuerpoGrande_413`
- ⚠️ `Verificacion_ResultadoNoPrevisto_Error500` — renombrada `Verificacion_AutorizadorLanza_Error500` (`VerificacionApiTests.cs:322`); el resultado no previsto lo cubre `ResponderVerificacion_ResultadosPrevistos_SuCodigo` (`CierrePorDefectoPresupuestosTests.cs:465`)
- ✅ `EmisionesPendientes_SoloNumeros`
- ✅ `Get_ConObservacion_SinDni`

Block 7:
- ✅ `Presupuesto_ConPendiente_MuestraVerificarYNoFacturar`
- ✅ `Presupuesto_ImporteDistinto_MuestraAvisoYDescartar`
- ✅ `Presupuesto_DescartarConfirmado_MuestraFacturar`
- ✅ `Presupuesto_DescartarCancelado_SigueVerificar`
- ✅ `Presupuesto_SinRespuesta_MensajeYSigueVerificar`
- ✅ `Presupuesto_SinNumero_Aviso`
- ✅ `Presupuesto_OtroEntorno_Aviso`
- ✅ `Presupuesto_Espera_MuestraSegundos`
- ✅ `Presupuesto_SinSesion_VaAIngreso`
- ✅ `Inicio_ConPendientes_Aviso155y160`
- ✅ `Inicio_SinPendientes_SinAviso`
- ✅ `Inicio_FallaAlListar_SinAvisoNiError`

Block 8:
- ✅ `e2e`
- ✅ `los`

## Alcance y registros cerrados
- ✅ Sin ampliación de alcance: solo verificación, descarte y lista de pendientes; el descarte borra solo la pendiente y la observación, nunca una `Factura`; ningún endpoint modifica facturas con CAE ni presupuestos Final
- ✅ Triggers: `CierrePresupuestos` y `Facturas` sin cambios; `RecuperacionEmisiones` no tiene `DROP TRIGGER`; `MigracionRecuperacionEmisionesTests.cs:107` confirma `TR_EmisionPendiente_Inmutable`

## Datos personales
- ✅ Tablas nuevas (`ObservacionEmision`, `DescarteEmision`) sin DNI ni datos del cliente; `EmisionPendiente` suma solo `ReceptorIdentificado` (0/1)
- ✅ Archivo del simulador sin DNI (`SimuladorArcaTests.cs:678`)
- ✅ Respuestas sin DNI (`Get_ConObservacion_SinDni`) y descripción de rechazo enmascarada (`Rechazo_DescripcionConDni_409SinDni`, D-34)
- ✅ Logs sin DNI (`Consultar_RespuestaConDni_NiLogNiResultadoLoLlevan`, D-14)

Total: 7 advertencias que no bloquean (criterio manual de homologación, Block 6 renombrada, W-VER-02, tres W-VER-03) y ningún ítem rojo.

Result: PASSED
