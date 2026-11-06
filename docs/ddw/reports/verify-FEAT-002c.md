# Verification FEAT-002c

Verificación cruzada del módulo completo (ronda 1), sobre `540bf49`, por un verificador que no escribió el código,
con corridas propias en una copia del repositorio: .NET 1189/1189, web 455/455, e2e unidad 49/49 y e2e 68/68 en
Chrome y Edge (1761/1761).

| Field | Value |
|---|---|
| Module | recálculo del catálogo al grabar la configuración (`src/Optica.Domain/Catalogo`, `src/Optica.Application/Catalogo/GrabarConfiguracionConRecalculo.cs`, `src/Optica.Infrastructure/Persistencia/UnidadDeTrabajoEf.cs` y `ArticuloRepository.cs`, `src/Optica.Api/Controllers/ConfiguracionController.cs`, `web/src/paginas/Configuracion.tsx`) |
| Line coverage | 95.3% |
| Branch coverage | 88.0% |
| Function coverage | 98.6% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet build Optica.sln --no-incremental` — 0 warnings; `dotnet format Optica.sln --verify-no-changes --no-restore` — clean; `npm run build` en `web/` (tsc + vite) — clean |

Cobertura de la tabla: código nuevo o modificado de .NET en `src/` (15 archivos existentes; el decimosexto,
`GrabarConfiguracionNegocio.cs`, se retiró), líneas 958/1005, ramas 257/292, funciones 142/144, recalculada por el
verificador. Total de .NET sin migraciones: 95,3 % / 91,4 % / 94,9 %. Web: líneas 99,25 %, ramas 96,91 %,
funciones 100 %.

## Acceptance criteria
- ✅ AC-01 — `ConPrecioRecalculado_AlicuotaDistinta_SinCambio`, `Grabar_Alicuota10coma5_CantidadCero`, `Alta_DespuesDeCambiarAlicuota_1105y50_1657coma50`, `Alta_DespuesDeCambiarMultiplo50_1105y50_1700`, e2e `alicuota 10,5 y luego costo 1.105 con margen 50 graba 1.657,50` (src/Optica.Application/Catalogo/GrabarConfiguracionConRecalculo.cs:GrabarEnTransaccionAsync, AltaArticulo.cs)
- ✅ AC-02 — `ConPrecioRecalculado_Multiplo50_1815A1850`, `Grabar_Multiplo50_Recalcula1815A1850`, `Post_Multiplo50_200ConPreciosCambiados`, `Configuracion_Grabada_MuestraCantidad`, e2e `multiplo 50 pasa 1.815,00 a 1.850,00 sin editar el articulo` (src/Optica.Domain/Catalogo/Articulo.cs:ConPrecioRecalculado, ArticuloRepository.cs:RegistrarPrecioRecalculado)
- ✅ AC-03 — `ConPrecioRecalculado_Inactivo_SeRecalcula`, `ListarParaRecalculo_DevuelveActivosEInactivos`, `RegistrarPrecioRecalculado_Inactivo_GrabaPrecioYVersion`, `Grabar_RecalculaInactivos`, e2e `el recalculo cambia tambien el precio de un articulo inactivo`
- ✅ AC-04 — `ConPrecioRecalculado_PrecioFueraDeRango_Rechazado`, `EjecutarEnTransaccion_OperacionLanza_RevierteTodo`, `EjecutarEnTransaccion_ConflictoDeUnicidad_NoGrabaNada`, `Grabar_PrecioFueraDeRango_RechazadoConCodigos`, `Grabar_MasDe10FueraDeRango_RechazadoCon10Codigos`, `Grabar_InactivoFueraDeRango_RechazadoConMarca`, `Grabar_FallaAlConfirmar_NoRegistraNiGraba`, `Post_PrecioFueraDeRango_400EnMultiplo`, `Configuracion_Error500_MensajeGeneral`, e2e `multiplo 50 rechazado con el codigo del articulo y nada cambia` (UnidadDeTrabajoEf.cs:EjecutarEnTransaccionAsync, ValidacionArticulo.cs:PrecioVentaRecalculadoFueraDeRango)
- ✅ AC-05 — `Volumen10000_AltaModificacionBusquedaYDesactivacion_MismosResultados`, `Volumen10000_Recalculo_Multiplo50`, `Volumen10000_Busqueda_P95MenorA1s`, `Siembra_DatoInvalido_FallaAntesDeMedir`

FR-01 (AC-01 a AC-03; D-07 con `CambiaParametrosDePrecio_*` y `Grabar_SoloTope_NoRecalcula`), FR-02
(`EjecutarEnTransaccion_ConfirmaLecturasYGrabacionJuntas` y las pruebas de AC-04) y NFR-01 (AC-05 y D-02) cubiertos.
Las pruebas verifican datos (precios, versiones, estado, textos exactos), no solo códigos de estado.

## Spec blocks
- ✅ Block 1 — 7/7 pruebas requeridas (dominio: `ConPrecioRecalculado`, `CambiaParametrosDePrecioRespectoDe`, `ResultadoRecalculo`)
- ✅ Block 2 — 9/9 pruebas requeridas (`EjecutarEnTransaccionAsync`, `ListarParaRecalculoAsync`, `RegistrarPrecioRecalculado`, dobles D-19 y D-20)
- ✅ Block 3 — 19/19 pruebas requeridas (`GrabarConfiguracionConRecalculo`, alta y modificación en la transacción, prueba de arquitectura de D-16); `Grabar_Invalido_NoRegistraNiConfirma` absorbida por `Grabar_DatosInvalidos_NoLeeNiGraba`, que es un superconjunto
- ✅ Block 4 — 8/8 pruebas requeridas (`ConfiguracionGrabada`, lector web, mensaje de D-06)
- ✅ Block 5 — 4/4 pruebas requeridas (siembra, AC-05, p95, recálculo de 10.000)
- ✅ Block 6 — 4/4 pruebas requeridas (e2e AC-01 a AC-04 en Chrome y Edge)

## Required tests (51/51, todas presentes y en verde)
- ✅ Block 1: `ConPrecioRecalculado_PrecioFueraDeRango_Rechazado`, `CambiaParametrosDePrecio_AnteriorNulo_LanzaError`, `ConPrecioRecalculado_Multiplo50_1815A1850`, `ConPrecioRecalculado_Inactivo_SeRecalcula`, `ConPrecioRecalculado_AlicuotaDistinta_SinCambio`, `CambiaParametrosDePrecio_SoloTope_Falso`, `CambiaParametrosDePrecio_AlicuotaCondicionOMultiplo_Verdadero`
- ✅ Block 2: `EjecutarEnTransaccion_Anidada_LanzaError`, `EjecutarEnTransaccion_OperacionLanza_RevierteTodo`, `EjecutarEnTransaccion_ConflictoDeUnicidad_NoGrabaNada`, `RegistrarPrecioRecalculado_SinCodigo_Rechazado`, `EjecutarEnTransaccion_ConfirmaLecturasYGrabacionJuntas`, `RegistrarPrecioRecalculado_Inactivo_GrabaPrecioYVersion`, `RegistrarPrecioRecalculado_SoloCambiaPrecioYVersion`, `ListarParaRecalculo_DevuelveActivosEInactivos`, `EjecutarEnTransaccion_ConflictoAtendido_ConfirmaSinCambios`
- ✅ Block 3: `Grabar_DatosInvalidos_NoLeeNiGraba`, `Grabar_PrecioFueraDeRango_RechazadoConCodigos`, `Grabar_MasDe10FueraDeRango_RechazadoCon10Codigos`, `Grabar_InactivoFueraDeRango_RechazadoConMarca`, `Grabar_FallaAlConfirmar_NoRegistraNiGraba`, `Grabar_SinFila_PropagaError`, `Grabar_Multiplo50_Recalcula1815A1850`, `Grabar_RecalculaInactivos`, `Grabar_SoloTope_NoRecalcula`, `Grabar_Alicuota10coma5_CantidadCero`, `Grabar_RegistraEnElLogDespuesDeConfirmar`, `Alta_DespuesDeCambiarAlicuota_1105y50_1657coma50`, `Alta_DespuesDeCambiarMultiplo50_1105y50_1700`, `Grabar_Exitoso_RegistraValoresAnterioresYNuevos`, `Grabar_ConflictoAlConfirmar_SePropaga`, `Grabar_VariosCamposInvalidos_DevuelveTodosLosErrores`, `Grabar_TopeAusente_GrabaSinTope`, `Grabar_Monotributo_ObtenerDevuelveMonotributo`, `Configuracion_NoUsaTiposDeCatalogo`
- ✅ Block 4: `Post_PrecioFueraDeRango_400EnMultiplo`, `Configuracion_PreciosCambiadosInvalido_RespuestaIlegible`, `Configuracion_Error500_MensajeGeneral`, `Post_Multiplo50_200ConPreciosCambiados`, `Get_Configuracion_SinCambiosEnElContrato`, `consultarConfiguracion_SinPreciosCambiados_SeLee`, `Appsettings_GrabacionDeConfiguracion_VaAlRegistroDeEventos`, `Configuracion_Grabada_MuestraCantidad`
- ✅ Block 5: `Siembra_DatoInvalido_FallaAntesDeMedir`, `Volumen10000_AltaModificacionBusquedaYDesactivacion_MismosResultados`, `Volumen10000_Recalculo_Multiplo50`, `Volumen10000_Busqueda_P95MenorA1s`
- ✅ Block 6: `alicuota 10,5 y luego costo 1.105 con margen 50 graba 1.657,50`, `multiplo 50 pasa 1.815,00 a 1.850,00 sin editar el articulo`, `el recalculo cambia tambien el precio de un articulo inactivo`, `multiplo 50 rechazado con el codigo del articulo y nada cambia`

## Tests
- ✅ Sad-path tests: POST `/api/configuracion` (`Post_PrecioFueraDeRango_400EnMultiplo`, `Post_Alicuota101_*`, `Post_CuerpoIlegible_*`, 401, 413, 415); `ConPrecioRecalculado_VigenteNula_LanzaError`; `CambiaParametrosDePrecio_AnteriorNulo_LanzaError`; `EjecutarEnTransaccion_Anidada_LanzaError`, `EjecutarEnTransaccion_CambiosSinConfirmar_Rechazado`, `EjecutarEnTransaccion_ReversionFalla_PropagaLaOriginal`; `RegistrarPrecioRecalculado_SinCodigo_Rechazado`, `RegistrarPrecioRecalculado_EntradaSeguidaConOtrosCambios_Rechazado`; lector web `grabarConfiguracion_PreciosCambiados_%s_RespuestaIlegible` y `Configuracion_PreciosCambiadosInvalido_RespuestaIlegible`
- ✅ Dead code (W-VER-01): sin código muerto ni imports sin uso en el diff; 4 usings sin uso previos al ticket en pruebas de Api, fuera del diff

## Warnings
- ⚠️ W-VER-02 — `GrabarConfiguracionConRecalculo.cs`: 86,4 % de ramas (19/22), 97,8 % de líneas; faltan guardas defensivas (`Invalido` con lista vacía, `fabricaRegistros ?? throw`); `EjecutarAsync` y `GrabarEnTransaccionAsync` al 100 %
- ⚠️ W-VER-03 — las pruebas de tiempo (`Volumen10000_Busqueda_P95MenorA1s`, `Volumen10000_Recalculo_Multiplo50`) corren en la suite en paralelo entre proyectos: riesgo aceptado en D-21, márgenes medidos amplios (p95 12–41 ms, recálculo 644 ms)
- ⚠️ W-VER-03 — las e2e de `configuracion-recalculo.spec.ts` dependen del orden (AC-04 última, D-24) y de la base del proyecto (`cantidad >= 1` en AC-02 y AC-03); intencional y documentado
- ⚠️ W-VER-03 — intermitencia de `[msedge-acceso] acceso.spec.ts:198` (bloqueo de FEAT-001b ante un retroceso del reloj, causa probable sin confirmar): no atribuible a FEAT-002c (no toca `src/` de Acceso); el harness pasó 68/68 en esta verificación; la falla de la corrida 1 del cierre no quedó identificada; va a un ticket FIX aparte por decisión del usuario
- ⚠️ F-VER-01 (trazabilidad) — ante un 500, AC-04 se cumple con el mensaje general "No se pudo conectar con el sistema. Reintentá.", que no dice que el cambio no se grabó; la spec aprobada (Block 4) lo eligió así
- ⚠️ F-VER-02 (trazabilidad) — la siembra quedó en `tests/Compartido/SiembraCatalogo.cs` y no en `tests/Optica.Api.Tests/Catalogo/` como dice la spec del Block 5 (corrección del arch-auditor, registrada en la revisión del bloque)
- ⚠️ Evidencia de TDD — Block 4: `Post_PrecioFueraDeRango_400EnMultiplo` y `Appsettings_GrabacionDeConfiguracion_VaAlRegistroDeEventos` pasaban antes de implementar (regresión, las matan mutantes); Block 6: e2e sobre código ya hecho, rojo inducido con mutaciones

## Notas
- NFR-02 (versión anterior de cada navegador): verificación manual pendiente, igual que en FEAT-002a y FEAT-002b.
- Decisiones tomadas en CODE sin contradecir el PRD: D-25, D-27, D-28 y D-30 (usuario), D-26 (agente), D-29 (ratificada por el usuario).
- Alerta de datos: la alícuota, la condición fiscal y el tope llegan al Registro de eventos de Windows (R-04 de FEAT-001c, aceptado) y a la salida del harness e2e (valores de prueba); conviene restringir el acceso a ese registro en la instalación y no publicar logs de corridas con datos reales.

Result: PASSED
