# Verification FEAT-007

Verificación del módulo completo de FEAT-007 (Emisión de factura a consumidor final, ARCA WSFEv1) sobre `309f112`, con
un verificador independiente (ddw-module-verifier, que no escribió el código), contra el PRD
(`docs/ddw/prd/prd-FEAT-007.md`, 32 AC), la spec (`docs/ddw/specs/spec-FEAT-007.md`, 8 bloques) y las decisiones
D-01 a D-66. El verificador volvió a correr toda la suite: .NET 2217/2217, web 1023/1023, e2e unidad 80/80 y e2e
158/158 en Chrome y Edge.

| Field | Value |
|---|---|
| Module | `src/Optica.Domain/Facturacion`, `src/Optica.Application/Facturacion`, `src/Optica.Infrastructure/Arca`, `src/Optica.Infrastructure/Facturacion`, persistencia de facturas y configuración, `PresupuestosController` y `ConfiguracionController`, `web/src/paginas/Presupuesto.tsx` y `Configuracion.tsx`, `web/src/api/cliente.ts`, e2e de facturación |
| Line coverage | 95.6% |
| Branch coverage | 87.0% |
| Function coverage | 98.2% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet build Optica.sln` — 0 warnings; `dotnet format Optica.sln --verify-no-changes` — clean; `npm run lint` (oxlint) — clean; `npm run build` (tsc + vite) — clean |

Cobertura de la tabla: archivos de producción .NET del ticket (52), unión de los cuatro informes de coverlet sin
migraciones, medida por el verificador. Web: líneas 98,95 %, ramas 96,68 %, funciones 100 %.

## Acceptance criteria
- ✅ AC-01 — `Armar_TotalDelPresupuesto_3Lineas3000`, `Emitir_Final3Lineas_FacturaConLineasYTotal3000`, `Emitir_Final3Lineas_201ConLaFactura`, e2e "facturación: emitir Factura B y verla en el presupuesto" (`ReglasEmision.Armar`, `EmitirFactura.EjecutarAsync`)
- ✅ AC-02 — `Armar_Borrador_Rechazado`, `Emitir_Borrador_Conflicto409SinLlamarArca`, e2e "facturación: Borrador y pedido directo a la Api rechazados"
- ✅ AC-03 — `Emitir_YaFacturado_Conflicto409SinLlamarArca`, `Agregar_SegundaFacturaMismoPresupuesto_ConflictoDuplicado`, e2e "Borrador y pedido directo…"
- ✅ AC-04 — `Armar_ResponsableInscripto_FacturaB`, `Wsfe_FacturaB_XmlConIvaYDni`
- ✅ AC-05 — `Armar_Monotributo_FacturaC`, `Emitir_CondicionCambiadaAMonotributo_FacturaC`, `Wsfe_FacturaC_XmlSinIva`
- ✅ AC-06 — `Armar_FechaDelDia`, `Emitir_FechaDelDia30_10_2026`
- ✅ AC-07 — `Armar_ProductosYServicios_TresFechasIguales`, `Wsfe_ProductosYServicios_XmlConFechas`
- ✅ AC-08 — `Armar_Productos_SinFechasDeServicio`
- ✅ AC-09 — `Armar_Total50000Tope100000ConDni_SinIdentificar`, `Wsfe_FacturaB_SinIdentificar_DocTipo99YDocNro0`
- ✅ AC-10 — `Armar_TotalIgualAlTope_SinIdentificar`
- ✅ AC-11 — `Armar_TotalUnCentavoSobreElTope_ConDni`, `Wsfe_FacturaB_XmlConIvaYDni`
- ✅ AC-12 — `Armar_Tope40000Total50000_ConDni`, `Emitir_TopeCambiadoA40000_UsaElVigente`
- ✅ AC-13 — `Armar_TopeFaltante_Rechazado`, `Emitir_TopeFaltante_Conflicto409SinLlamarArca`, e2e "facturación: configuración incompleta frena la emisión"
- ✅ AC-14 — `Importes_1815Alicuota21_Neto1500Iva315`, `Wsfe_FacturaB_XmlConIvaYDni`
- ✅ AC-15 — `Importes_1000Alicuota21_Neto826_45Iva173_55`
- ✅ AC-16 — `Importes_1105Alicuota10_5_Neto1000Iva105`, `Emitir_AlicuotaCambiadaA10_5_UsaLaVigente`
- ✅ AC-17 — `Importes_FacturaC_SoloTotal`, `Wsfe_FacturaC_XmlSinIva`
- ✅ AC-18 — `Emitir_Autorizada_GuardaFacturaYQuitaPendiente`, `Agregar_Factura_SeLeeConSusLineasYEntorno`, `Autorizador_ResultadoA_Autorizada`, "Facturar: Final con concepto Productos emite y muestra el comprobante"
- ✅ AC-19 — `NumeroComprobante_B3_125_Formato`, `ObtenerPresupuesto_ConFactura_MuestraComprobante`
- ✅ AC-20 — `Emitir_Rechazada_SinFacturaYSinPendiente`, `Autorizador_ResultadoR_RechazadaConCodigo`, `Simulador_Rechazar_DevuelveCodigoYDescripcion`, e2e "facturación rechazo: mensaje de ARCA y Facturar habilitado"
- ✅ AC-21 — `Emitir_SinRespuesta30s_TimeoutDejaPendienteSinReintentar`, `Autorizador_Timeout30sDespuesDeEnviar_SinRespuesta`, e2e "facturación sin respuesta: mensaje y pendiente al volver a facturar"
- ✅ AC-22 — `Emitir_ConPendiente_Conflicto409SinLlamarArca`, e2e "facturación sin respuesta…"
- ✅ AC-23 — `Update_Factura_RechazadoRegistroCerrado`, `Delete_Factura_RechazadoRegistroCerrado`, `Update_LineaFactura_RechazadoRegistroCerrado`, `Insert_LineaEnFacturaCompleta_RechazadoRegistroCerrado`, `PutYDeleteFactura_NoExisten_Rechazado405o404`
- ⚠️ AC-24 — `Autorizador_Homologacion_PuntoVentaDeLaConfiguracionSinCertificadoEnPantalla` con respuestas grabadas de homologación (punto de venta de la configuración, solo hosts de homologación, sin certificado en pantalla); la obtención de un CAE real queda pendiente de verificación manual del usuario con el certificado de homologación (D-02, registrado en el PRD)
- ✅ AC-25 — `Emitir_20Facturas_DiferenciaMenorA2sEn19` (diferencias entre 11 y 84 ms)
- ✅ AC-26 — los e2e de facturación y configuración pasan en los proyectos de Chrome y Edge del harness (158/158; D-03)
- ✅ AC-27 — `EvaluarParaGrabar_Alicuota19_Rechazado`, `Grabar_Alicuota19PorApi_Rechazado400`, e2e "configuración: alícuota 19 por la Api rechazada"
- ✅ AC-28 — `Get_Configuracion_DevuelveAlicuotasAdmitidasYPuntoVenta`, "alicuota lista: muestra las seis y la guardada elegida"
- ✅ AC-29 — `Armar_Alicuota19ResponsableInscripto_Rechazado`, `Emitir_Alicuota19_Conflicto409SinLlamarArca`
- ✅ AC-30 — `Emitir_PuntoVenta3_Numero125_Comprobante`, e2e "facturación: emitir Factura B y verla en el presupuesto" (0003-00000125)
- ✅ AC-31 — `EvaluarParaGrabar_PuntoVentaFueraDeRango_Rechazado`, `Grabar_PuntoVentaConDecimales_Invalido400`, `Migracion_PuntoVenta_CheckRechazaCero`, "punto de venta 0, 100000 y 2,5: inválido sin enviar"
- ✅ AC-32 — `Armar_PuntoVentaFaltante_Rechazado`, `Emitir_PuntoVentaFaltante_Conflicto409SinLlamarArca`, e2e "configuración incompleta…"

## Spec blocks
- ✅ Block 1 — configuración: alícuotas de ARCA y punto de venta; 13/13 pruebas requeridas
- ✅ Block 2 — pantalla de configuración; 7/7
- ✅ Block 3 — dominio de facturación; 22/22 (el rechazo tipado se llama `ResultadoArmadoEmision`, D-44)
- ✅ Block 4 — persistencia de facturas y emisiones pendientes; 11/11 (seis triggers, D-48)
- ✅ Block 5 — puerto de ARCA, opciones y simulador; 9/9
- ✅ Block 6 — cliente de WSAA y WSFEv1; 19/19 (Pkcs 10.0.13, D-56, ADR-024)
- ✅ Block 7 — caso de uso y endpoint de emisión; 31/31
- ✅ Block 8 — pantalla de facturación y e2e; 14/14

Las 126 pruebas que lista la spec existen con su nombre exacto (las parametrizadas, como prefijo). Cada bloque pasó
el verificador y el auditor de arquitectura (`.ddw-work/review-block-1.md` a `review-block-8.md`), con evidencia de
pruebas en rojo antes de implementar.

## Tests
- ✅ Sad-path tests: `POST /api/presupuestos/{numero}/factura` con concepto faltante o inválido (400), presupuesto inexistente (404), sin sesión (401), cuerpo grande (413), sin JSON (415), PUT/DELETE/PATCH (405), los 10 motivos de 409 y `Emitir_AutorizadorLanza_500ConLaPendienteGuardada`; `POST /api/configuracion` con alícuota 19 y punto de venta 0, 100000 y 2,5 rechazados; lectores estrictos de `cliente.ts` con respuestas ilegibles
- ✅ Lint y tipos: build sin advertencias, formato, oxlint y tsc limpios

## Required tests (spec)

Block 1:
- ✅ `EvaluarParaGrabar_Alicuota19_Rechazado`
- ✅ `EvaluarParaGrabar_LasSeisAlicuotas_Validas`
- ✅ `EvaluarParaGrabar_PuntoVentaFueraDeRango_Rechazado`
- ✅ `Grabar_PuntoVentaConDecimales_Invalido400`
- ✅ `Grabar_Alicuota19PorApi_Rechazado400`
- ✅ `Grabar_PuntoVentaAusente_ConservaElGuardado`
- ✅ `Obtener_AlicuotaGuardada19_SeLeeNoAdmitida`
- ✅ `Obtener_PuntoVentaGuardadoCero_ErrorDeEstadoCorrupto`
- ✅ `Get_Configuracion_DevuelveAlicuotasAdmitidasYPuntoVenta`
- ✅ `Migracion_PuntoVenta_CheckRechazaCero`
- ✅ `SinDatosArca_Configuracion_PermitePuntoVentaYRechazaCertificado`
- ✅ `Reemplazar_AlicuotaNoAdmitida_Rechazado` / `ValidacionConfiguracion`
- ✅ `Grabar_PuntoVentaAusente_RegistroConAnteriorYNuevo`

Block 2:
- ✅ `alicuota lista: muestra las seis y la guardada elegida`
- ✅ `alicuota guardada 19: ninguna elegida y aviso, envío rechazado`
- ✅ `punto de venta 0, 100000 y 2,5: inválido sin enviar`
- ✅ `400 de la Api en puntoVenta: error junto al campo`
- ✅ `leerConfiguracion: respuesta sin alicuotasAdmitidas es inválida`
- ✅ `configuración: elegir alícuota y punto de venta y grabar`
- ✅ `configuración: alícuota 19 por la Api rechazada`

Block 3:
- ✅ `Armar_ResponsableInscripto_FacturaB`
- ✅ `Armar_Monotributo_FacturaC`
- ✅ `Armar_TotalDelPresupuesto_3Lineas3000`
- ✅ `Armar_FechaDelDia`
- ✅ `Armar_ProductosYServicios_TresFechasIguales`
- ✅ `Armar_Productos_SinFechasDeServicio`
- ✅ `Armar_Total50000Tope100000ConDni_SinIdentificar`
- ✅ `Armar_TotalIgualAlTope_SinIdentificar`
- ✅ `Armar_TotalUnCentavoSobreElTope_ConDni`
- ✅ `Armar_Tope40000Total50000_ConDni`
- ✅ `Importes_1815Alicuota21_Neto1500Iva315`
- ✅ `Importes_1000Alicuota21_Neto826_45Iva173_55`
- ✅ `Importes_1105Alicuota10_5_Neto1000Iva105`
- ✅ `Importes_FacturaC_SoloTotal`
- ✅ `Armar_Borrador_Rechazado`
- ✅ `Armar_TopeFaltante_Rechazado`
- ✅ `Armar_PuntoVentaFaltante_Rechazado`
- ✅ `Armar_Alicuota19ResponsableInscripto_Rechazado`
- ✅ `Factura_CaeInvalido_Rechazado`
- ✅ `NumeroComprobante_B3_125_Formato`
- ✅ `Receptor_ToString_SinDni`
- ✅ `Dependencias_PresupuestosNoDependeDeFacturacion`

Block 4:
- ✅ `Agregar_Factura_SeLeeConSusLineasYEntorno`
- ✅ `Agregar_SegundaFacturaMismoPresupuesto_ConflictoDuplicado`
- ✅ `Agregar_SegundaPendiente_ConflictoDuplicado`
- ✅ `Update_Factura_RechazadoRegistroCerrado`
- ✅ `Delete_Factura_RechazadoRegistroCerrado`
- ✅ `Update_LineaFactura_RechazadoRegistroCerrado`
- ✅ `Check_ImportesQueNoSuman_Rechazado` / `CK_Factura_Importes`
- ✅ `Obtener_FilaIlegible_ErrorSinValores`
- ✅ `Quitar_Pendiente_YaNoExiste`
- ✅ `Insert_LineaEnFacturaCompleta_RechazadoRegistroCerrado`
- ✅ `Migraciones_TodasAplicadas_LosCincoTriggersExisten`

Block 5:
- ✅ `Opciones_SinEntorno_Simulado`
- ✅ `Opciones_EntornoInvalido_Rechazado`
- ✅ `Opciones_TiempoMaximoInvalido_Rechazado`
- ✅ `Opciones_CuitInvalido_Rechazado`
- ✅ `Opciones_HomologacionSinCertificado_NoEnviada`
- ✅ `Simulador_Autorizar_NumeraConsecutivoPorTipoYPuntoVenta`
- ✅ `Simulador_Rechazar_DevuelveCodigoYDescripcion`
- ✅ `Simulador_NoResponder_TimeoutPorCancelacion`
- ✅ `Simulador_ArchivoIlegible_NoEnviadaError`

Block 6:
- ✅ `Wsaa_FirmaCms_VerificableConElCertificado`
- ✅ `Wsaa_TicketVigente_SeReutiliza`
- ✅ `Wsaa_TicketGuardado_CifradoEnDisco`
- ✅ `Dependencias_SoloInfrastructureArcaUsaTiposDeArca` / `tests/Optica.Api.Tests/Arquitectura/AislamientoArcaTests.cs` / `SimuladorArca` / `ClienteWsfe` / `Optica.Infrastructure.Arca` / `ConfiguracionInfraestructura`
- ✅ `Wsaa_TicketGuardadoIlegible_SePideOtro`
- ✅ `Wsaa_Rechazo_NoEnviadaError`
- ✅ `Wsaa_SinRespuesta_NoEnviadaTimeout`
- ✅ `Wsfe_FacturaB_XmlConIvaYDni`
- ✅ `Wsfe_FacturaC_XmlSinIva`
- ✅ `Wsfe_ProductosYServicios_XmlConFechas`
- ✅ `Wsfe_UltimoAutorizado_Error_NoEnviada`
- ✅ `Autorizador_ResultadoA_Autorizada`
- ✅ `Autorizador_ResultadoR_RechazadaConCodigo`
- ✅ `Autorizador_Timeout30sDespuesDeEnviar_SinRespuesta`
- ✅ `Autorizador_RespuestaIlegible_SinRespuestaError`
- ✅ `Autorizador_PfxClaveIncorrecta_NoEnviadaError`
- ✅ `Autorizador_XmlConDtd_Rechazado`
- ✅ `Autorizador_Log_SinTokenNiDniNiClave`
- ✅ `Autorizador_Homologacion_PuntoVentaDeLaConfiguracionSinCertificadoEnPantalla`

Block 7:
- ✅ `Emitir_Final3Lineas_FacturaConLineasYTotal3000`
- ✅ `Emitir_Borrador_Conflicto409SinLlamarArca`
- ✅ `Emitir_YaFacturado_Conflicto409SinLlamarArca`
- ✅ `Emitir_FechaDelDia30_10_2026`
- ✅ `Emitir_TopeCambiadoA40000_UsaElVigente`
- ✅ `Emitir_TopeFaltante_Conflicto409SinLlamarArca`
- ✅ `Emitir_AlicuotaCambiadaA10_5_UsaLaVigente`
- ✅ `Emitir_CondicionCambiadaAMonotributo_FacturaC`
- ✅ `Emitir_Autorizada_GuardaFacturaYQuitaPendiente`
- ✅ `ObtenerPresupuesto_ConFactura_MuestraComprobante`
- ✅ `Emitir_Rechazada_SinFacturaYSinPendiente`
- ✅ `Emitir_SinRespuesta30s_TimeoutDejaPendienteSinReintentar`
- ✅ `Emitir_ConPendiente_Conflicto409SinLlamarArca`
- ✅ `PutYDeleteFactura_NoExisten_Rechazado405o404`
- ✅ `Emitir_Alicuota19_Conflicto409SinLlamarArca`
- ✅ `Emitir_PuntoVenta3_Numero125_Comprobante`
- ✅ `Emitir_PuntoVentaFaltante_Conflicto409SinLlamarArca`
- ✅ `Emitir_ConceptoInvalido_400`
- ✅ `Emitir_PresupuestoInexistente_404Error`
- ✅ `Emitir_NoEnviada_SinPendienteError`
- ✅ `Emitir_OtraEmisionEnCurso_NoEnviadaTimeout`
- ✅ `Emitir_DosPedidosSimultaneos_UnoConflictoPendiente`
- ✅ `Emitir_AutorizadaConConflictoAlGrabar_409PendienteVerificacionError`
- ✅ `Emitir_PedidoHttpCancelado_LaFacturaAutorizadaSeGraba`
- ✅ `Emitir_CandadoYArca_UnSoloPlazoDe30s`
- ✅ `Emitir_CandadoConsumioElPlazo_NoEnviadaSinLlamarArca`
- ✅ `ObtenerEstadoFacturacion_ConFacturaYSinFactura`
- ✅ `Emitir_20Facturas_DiferenciaMenorA2sEn19`
- ✅ `Emitir_Log_SinDniNiDatosDelCliente`
- ✅ `Proteccion_RutaFactura_SinSesion401` / `SinCache_RutaFactura_NoStore`

Block 8:
- ✅ `Facturar: Final con concepto Productos emite y muestra el comprobante`
- ✅ `Facturar: deshabilitado en Borrador, con factura y con pendiente`
- ✅ `Facturar: 409 rechazada muestra código y descripción y sigue habilitado`
- ✅ `Facturar: 409 sin_respuesta muestra que ARCA no respondió`
- ✅ `Facturar: 409 sin_tope, sin_punto_venta y alicuota_no_admitida, mensajes con enlace`
- ✅ `Facturar: 409 no_enviada y respuesta ilegible, error general`
- ✅ `Facturar: 409 pendiente_verificacion, mensaje de pendiente y botón deshabilitado`
- ✅ `Facturar: sin concepto elegido, Emitir deshabilitado`
- ✅ `Facturar: 401 vuelve al ingreso`
- ✅ `facturación: emitir Factura B y verla en el presupuesto`
- ✅ `facturación: Borrador y pedido directo a la Api rechazados`
- ✅ `facturación rechazo: mensaje de ARCA y Facturar habilitado`
- ✅ `facturación sin respuesta: mensaje y pendiente al volver a facturar`
- ✅ `facturación: configuración incompleta frena la emisión`
- ✅ Los e2e de facturación corren en los proyectos de Chrome y Edge del harness

## Warnings
- ⚠️ AC-24 pendiente de verificación manual con el certificado de homologación (D-02).
- ⚠️ W-VER-02: ramas de Application en 84,4 % (guardas de fábricas de `ResultadoAutorizacion` y `ResultadoEmision`).
- ⚠️ Siete archivos con menos de 80 % de ramas (`ConfiguracionController`, `AutorizadorWsfe`, `ClienteWsaa`,
  `ServiciosArca`, `FabricaConexion`, `TextosFacturacion`, `CertificadoArca`); el agregado del ticket es 87,0 %.
- ⚠️ W-VER-03: esperas reales cortas para afirmar que algo no terminó en `AutorizadorWsfeTests.cs:90` y
  `ClienteWsaaTests.cs` (4 lugares); `facturacion.spec.ts` depende del orden serial para el número 125 (D-65).
- Datos personales: el DNI se guarda sin cifrar en la base y en los backups (R-13, aceptado); la clave del PFX como
  variable de usuario queda sin cifrar en `HKCU\Environment` (advertido en la guía); R-10 aceptado por el usuario hasta el
  PRD 08 (D-63). Nada de eso llega al log.

Result: PASSED
