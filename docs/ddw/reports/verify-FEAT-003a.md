# Verification FEAT-003a

Ronda 2 (2026-11-07), sobre `01d2f08`. La ronda 1 (verificador independiente `ddw-module-verifier`, sobre
`6855ac9`: 0 FAIL, 8 WARN, 52 PASS) terminó en un lazo correctivo VERIFY → CODE → PLAN por decisión del usuario
para corregir W-05 (respuestas de `/api` sin `Cache-Control: no-store`, con datos personales del cliente). El
lazo agregó el Block 7 (`fa920fc` spec y threat, `7f890d6` código), revisado por el verificador y el auditor
(PASSED). Esta ronda reúne la trazabilidad de la ronda 1, que no cambió para los Blocks 1 a 6, con el Block 7 y
la suite completa del re-cierre de CODE (2170/2170).

| Field | Value |
|---|---|
| Module | Presupuestos (src/Optica.*/Presupuestos, PresupuestosController, SinCacheApi, web/src/paginas/NuevoPresupuesto y Presupuesto, e2e/tests/presupuestos-alta.spec.ts) |
| Line coverage | 96.1% |
| Branch coverage | 91.0% |
| Function coverage | 98.9% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean; `dotnet build Optica.sln` — 0 warnings; `npm run build` (tsc estricto) — clean |

Cobertura de la tabla: código nuevo o modificado de .NET en la rama (19 archivos de `src/`, sin migraciones).
.NET completo: 95.5% / 91.7% / 95.8%; web: 99.1% / 96.98% / 100%.

## Acceptance criteria
- ✅ AC-01 — `Evaluar_SeisDatosValidos_SinInfracciones`, `Alta_SeisDatosYUnaLinea_Grabado`, `Post_SeisDatos_201YGetLosMuestra`, `NuevoPresupuesto_Grabar_NavegaYConfirmaBorrador`, e2e "seis datos y una linea graban, confirman y se muestran al reabrir" (`AltaPresupuesto.EjecutarAsync`, `PresupuestosController.Grabar`)
- ✅ AC-02 — `Evaluar_SinDni_DniFaltante`, `Post_SinDni_400ConIngresaElDni`, `NuevoPresupuesto_ErroresPorCampo400_JuntoAlCampo`, e2e "sin DNI rechazado con Ingresá el DNI del cliente"
- ✅ AC-03 — `Evaluar_SinApellidoOSinNombre_InfraccionFaltante`, `Post_SinApellidoOSinNombre_400EnElCampo`, e2e "sin apellido o sin nombre rechazado junto al campo faltante"
- ✅ AC-04 — `Agregar_SinOpcionales_GuardaNull`, `Post_SinOpcionales_201`, `NuevoPresupuesto_SinOpcionales_Graba`, e2e "sin domicilio, email ni telefono graba"
- ✅ AC-05 — `NormalizarDni_23Punto456Punto789_23456789`, `Post_Dni23Punto456Punto789_GetDevuelve23456789`, `Presupuesto_MuestraDni23456789`, e2e "DNI 23.456.789 se guarda y se muestra 23456789"
- ✅ AC-06 — `Evaluar_Dni123456_123456789_23A56789_DniInvalido`, `Post_Dni123456_123456789_23A56789_400`, `NuevoPresupuesto_Dni123456_ErrorAntesDeEnviar`, e2e "DNI 123456, 123456789 o 23A56789 rechazado"
- ✅ AC-07 — `Evaluar_EmailAnaArrobaYAnaArrobaMail_EmailInvalido`, `Post_EmailAnaArrobaYAnaArrobaMail_400`, `NuevoPresupuesto_EmailAnaArroba_ErrorAntesDeEnviar`, e2e "email ana@ o ana@mail rechazado"
- ✅ AC-08 — `Evaluar_EmailAnaArrobaMailCom_Valido`, `Post_EmailAnaMailCom_201`, `NuevoPresupuesto_EmailAnaMailCom_Graba`, e2e "email ana@mail.com graba"
- ✅ AC-09 — `Siguiente_Ultimo154_155`, `ObtenerUltimoNumero_Con154_Devuelve154`, `Post_Ultimo154_Numero155`, `Presupuesto_MuestraNumero155`, e2e "despues del 154 el siguiente es el 155"
- ✅ AC-10 — `Siguiente_SinPresupuestos_1`, `Post_BaseVacia_Numero1`, e2e "el primer presupuesto de la base recibe el numero 1"
- ✅ AC-11 — `Nuevo_SinLineas_Rechazado`, `Post_SinLineas_400EnLineas`, `NuevoPresupuesto_SinLineas_ErrorSinEnviar`, e2e "sin lineas rechazado"
- ✅ AC-12 — `Nuevo_QuedaEnBorrador`, `Post_EstadoBorrador`, e2e "queda en Borrador y el mensaje de confirmacion lo dice"
- ✅ AC-13 — `BuscarAsync_SoloActivosArmazon_ExcluyeInactivos`, `Get_Listado_SoloActivosArmazon_SinInactivos`, `NuevoPresupuesto_Buscar_PideSoloActivos`, e2e "buscar armazon lista activos con y sin tilde y ningun inactivo"
- ✅ AC-14 — `BuscarAsync_SoloActivosPorCodigo_ListaElArticulo`, `Get_Listado_SoloActivosPorCodigo`, e2e "buscar por codigo lista el articulo activo"
- ✅ AC-15 — `DesdeArticulo_1850_CopiaCodigoDescripcionYPrecio`, `Post_Articulo1850_Linea1850Cantidad1Descuento0`, `NuevoPresupuesto_Agregar1850_PrecioUnitarioSoloLectura`, e2e "articulo de 1.850,00 completa la linea con precio unitario de solo lectura"
- ✅ AC-16 — `DesdeArticulo_PrecioMenos150_Rechazado`, `Post_PrecioMenos150_400EnLaLinea`, `NuevoPresupuesto_ArticuloMenos150_NoSeAgrega`, e2e "articulo de -150,00 rechazado: no se agrega a la linea"
- ✅ AC-17 — `Presupuesto_Linea_MuestraLasSieteColumnas`, `Post_SeisDatos_201YGetLosMuestra`, e2e (AC-01 + AC-17)
- ✅ AC-18 — `Agregar_Linea1850_GuardaCentavosCantidad1YDescuento0`, `Post_Articulo1850_Linea1850Cantidad1Descuento0`, e2e "la linea se graba con cantidad 1, descuento 0 y precios de 1.850,00"
- ✅ AC-19 — `Alta_RelojLocal10_03_2026_GuardaEsaFecha`, `Post_Reloj10_03_2026_GetDevuelveEsaFecha`, `Presupuesto_MuestraFecha10_03_2026`, e2e "la fecha es la del dia y se muestra al reabrir"

## Spec blocks
- ✅ Block 1 — 26/26 pruebas de la spec (dominio), TDD con rojo por aserción
- ✅ Block 2 — 17/17 (infraestructura, migración `Presupuestos` sin reconstruir `Articulo`)
- ✅ Block 3 — 22/22 (aplicación, transacción de ADR-013/ADR-015)
- ✅ Block 4 — 27/27 (Api)
- ✅ Block 5 — 21/21 (web)
- ✅ Block 6 — 18/18 (e2e en Chrome y Edge, 104/104)
- ✅ Block 7 — 3/3 (`Api_RespuestasExitosas_NoStore`, `Api_RespuestasDeError_NoStore`, `Spa_ArchivosYRutas_SinNoStore`)

## Tests
- ✅ Sad-path tests: `Post_CuerpoIlegible_400`, `Post_Errores400_NoRepitenElValor`, `Get_Inexistente_404`, `Get_Listado_SoloActivosIlegible_400`, `Post_SinJson_415`, `Post_DemasiadoGrande_413`, `RutasNuevas_SinSesion_401`, `ObtenerAsync_FilaCorrupta_EsEstadoCorrupto`, `Api_RespuestasDeError_NoStore` cubren cada superficie de entrada
- ✅ Suite completa 2170/2170 (`docs/ddw/reports/tests-FEAT-003a.md`), SAST PASSED (`docs/ddw/security/sast-FEAT-003a.md`)

## Pruebas de la spec
Todas existen con el nombre de la spec y pasan en la corrida de `tests-FEAT-003a.md` (2170/2170).

- Block 1: ✅ `Evaluar_SinApellidoOSinNombre_InfraccionFaltante`, ✅ `Evaluar_SinDni_DniFaltante`, ✅ `Evaluar_Dni123456_123456789_23A56789_DniInvalido`, ✅ `Evaluar_EmailAnaArrobaYAnaArrobaMail_EmailInvalido`, ✅ `Evaluar_EmailExtensionDeUnaLetra_EmailInvalido`, ✅ `Evaluar_EmailAnaArrobaMailComAr_Valido`, ✅ `Evaluar_DomicilioYTelefonoLargos_Rechazados`, ✅ `Evaluar_Utf16Invalido_Rechazado`, ✅ `Evaluar_ApellidoConEspaciosAlrededor_SeRecorta`, ✅ `DesdeArticulo_PrecioMenos150_Rechazado`, ✅ `DesdeArticulo_Inactivo_Rechazado`, ✅ `Nuevo_SinLineas_Rechazado`, ✅ `ConstructorLinea_InvariantesRotas_Error`, ✅ `Siguiente_UltimoMaximo_Error`, ✅ `Evaluar_SeisDatosValidos_SinInfracciones`, ✅ `Evaluar_SinDomicilioEmailNiTelefono_SinInfracciones`, ✅ `NormalizarDni_23Punto456Punto789_23456789`, ✅ `Evaluar_EmailAnaArrobaMailCom_Valido`, ✅ `Siguiente_Ultimo154_155`, ✅ `Siguiente_SinPresupuestos_1`, ✅ `Nuevo_QuedaEnBorrador`, ✅ `DesdeArticulo_1850_CopiaCodigoDescripcionYPrecio`, ✅ `DesdeArticulo_1850_Cantidad1Descuento0YPreciosIguales`, ✅ `Nuevo_Fecha10_03_2026_LaConserva`, ✅ `DesdeArticulo_PrecioCero_SeAdmite`, ✅ `ToString_ClienteYPresupuesto_SinDatosPersonales`
- Block 2: ✅ `ObtenerAsync_FilaCorrupta_EsEstadoCorrupto`, ✅ `Agregar_SinNumero_Rechazado`, ✅ `Confirmar_NumeroRepetido_ConflictoDeUnicidad`, ✅ `Confirmar_ArticuloInexistente_FallaPorClaveForanea`, ✅ `Esquema_ChecksRechazanValoresInvalidos`, ✅ `ObtenerUltimoNumero_Con154_Devuelve154`, ✅ `ObtenerUltimoNumero_SinPresupuestos_Null`, ✅ `Agregar_SeisDatosYUnaLinea_SeLeeIgual`, ✅ `Agregar_SinOpcionales_GuardaNull`, ✅ `Agregar_Dni23456789_GuardaSinPuntos`, ✅ `Agregar_Borrador_GuardaBorrador`, ✅ `Agregar_Linea1850_GuardaCentavosCantidad1YDescuento0`, ✅ `Agregar_Fecha10_03_2026_GuardaTextoYLeeIgual`, ✅ `BuscarAsync_SoloActivosArmazon_ExcluyeInactivos`, ✅ `BuscarAsync_SoloActivosPorCodigo_ListaElArticulo`, ✅ `Migracion_Presupuestos_NoTocaArticuloNiSuSecuencia`, ✅ `Esquema_Presupuesto_ColumnasNotNullChecksYForeignKeys`
- Block 3: ✅ `Alta_SinApellidoSinNombreSinDni_TodosLosErroresSinLeer`, ✅ `Alta_Dni123456_ErrorEnDni`, ✅ `Alta_EmailAnaArroba_ErrorEnEmail`, ✅ `Alta_SinLineas_ErrorEnLineas`, ✅ `Alta_101LineasOCodigoFaltante_Rechazado`, ✅ `Alta_ArticuloInactivoOInexistente_ErrorEnLaLinea`, ✅ `Alta_PrecioMenos150_ErrorEnLaLinea`, ✅ `Alta_FallaAlConfirmar_SePropagaSinGrabar`, ✅ `Obtener_Inexistente_Null`, ✅ `Alta_SeisDatosYUnaLinea_Grabado`, ✅ `Alta_SinOpcionales_Grabado`, ✅ `Alta_DniConPuntos_Graba23456789`, ✅ `Alta_EmailAnaMailCom_Grabado`, ✅ `Alta_Ultimo154_Numero155`, ✅ `Alta_SinPresupuestos_Numero1`, ✅ `Alta_QuedaEnBorrador`, ✅ `Alta_Articulo1850_LineaConPrecioDelCatalogo`, ✅ `Alta_RelojLocal10_03_2026_GuardaEsaFecha`, ✅ `Alta_LeeArticulosNumeroYFechaDentroDeLaOperacion`, ✅ `Buscar_SoloActivos_PasaElFiltro`, ✅ `Alta_NoRegistraDatosDelCliente`, ✅ `Catalogo_NoUsaTiposDePresupuestos`
- Block 4: ✅ `Post_CuerpoIlegible_400`, ✅ `Post_SinDni_400ConIngresaElDni`, ✅ `Post_SinApellidoOSinNombre_400EnElCampo`, ✅ `Post_Dni123456_123456789_23A56789_400`, ✅ `Post_EmailAnaArrobaYAnaArrobaMail_400`, ✅ `Post_SinLineas_400EnLineas`, ✅ `Post_PrecioMenos150_400EnLaLinea`, ✅ `Post_Errores400_NoRepitenElValor`, ✅ `Get_Inexistente_404`, ✅ `Get_Listado_SoloActivosIlegible_400`, ✅ `Post_SinJson_415`, ✅ `Post_DemasiadoGrande_413`, ✅ `RutasNuevas_SinSesion_401`, ✅ `Traductores_ResultadoNoPrevisto_Lanzan`, ✅ `Post_SeisDatos_201YGetLosMuestra`, ✅ `Post_SinOpcionales_201`, ✅ `Post_Dni23Punto456Punto789_GetDevuelve23456789`, ✅ `Post_EmailAnaMailCom_201`, ✅ `Post_Ultimo154_Numero155`, ✅ `Post_BaseVacia_Numero1`, ✅ `Post_EstadoBorrador`, ✅ `Get_Listado_SoloActivosArmazon_SinInactivos`, ✅ `Get_Listado_SoloActivosPorCodigo`, ✅ `Post_Articulo1850_Linea1850Cantidad1Descuento0`, ✅ `Post_Reloj10_03_2026_GetDevuelveEsaFecha`, ✅ `Registro_AltaPresupuesto_SinDatosPersonales`, ✅ `Solicitudes_ToString_SinValores`
- Block 5: ✅ `NuevoPresupuesto_ErroresPorCampo400_JuntoAlCampo`, ✅ `NuevoPresupuesto_Dni123456_ErrorAntesDeEnviar`, ✅ `NuevoPresupuesto_EmailAnaArroba_ErrorAntesDeEnviar`, ✅ `NuevoPresupuesto_SinLineas_ErrorSinEnviar`, ✅ `NuevoPresupuesto_ArticuloMenos150_NoSeAgrega`, ✅ `Presupuesto_NumeroInvalidoO404_NoEncontrado`, ✅ `NuevoPresupuesto_RespuestaIlegible_MensajeGeneral`, ✅ `NuevoPresupuesto_413_Mensaje`, ✅ `NuevoPresupuesto_SinSesion401_VuelveAlIngreso`, ✅ `leerPresupuesto_EstadoDesconocido_RespuestaIlegible`, ✅ `NuevoPresupuesto_Grabar_NavegaYConfirmaBorrador`, ✅ `NuevoPresupuesto_SinOpcionales_Graba`, ✅ `Presupuesto_MuestraDni23456789`, ✅ `NuevoPresupuesto_EmailAnaMailCom_Graba`, ✅ `Presupuesto_MuestraNumero155`, ✅ `NuevoPresupuesto_Buscar_PideSoloActivos`, ✅ `NuevoPresupuesto_Agregar1850_PrecioUnitarioSoloLectura`, ✅ `Presupuesto_Linea_MuestraLasSieteColumnas`, ✅ `Presupuesto_MuestraFecha10_03_2026`, ✅ `NuevoPresupuesto_NoGuardaDatosEnElAlmacenamiento`, ✅ `Inicio_EnlaceNuevoPresupuesto`
- Block 6: ✅ `el primer presupuesto de la base recibe el numero 1`, ✅ `sin DNI rechazado con Ingresá el DNI del cliente`, ✅ `sin apellido o sin nombre rechazado junto al campo faltante`, ✅ `DNI 123456, 123456789 o 23A56789 rechazado`, ✅ `email ana@ o ana@mail rechazado`, ✅ `sin lineas rechazado`, ✅ `articulo de -150,00 rechazado: no se agrega a la linea`, ✅ `seis datos y una linea graban, confirman y se muestran al reabrir`, ✅ `sin domicilio, email ni telefono graba`, ✅ `DNI 23.456.789 se guarda y se muestra 23456789`, ✅ `email ana@mail.com graba`, ✅ `despues del 154 el siguiente es el 155`, ✅ `queda en Borrador y el mensaje de confirmacion lo dice`, ✅ `buscar armazon lista activos con y sin tilde y ningun inactivo`, ✅ `buscar por codigo lista el articulo activo`, ✅ `articulo de 1.850,00 completa la linea con precio unitario de solo lectura`, ✅ `la linea se graba con cantidad 1, descuento 0 y precios de 1.850,00`, ✅ `la fecha es la del dia y se muestra al reabrir`
- Block 7: ✅ `Api_RespuestasExitosas_NoStore`, ✅ `Api_RespuestasDeError_NoStore`, ✅ `Spa_ArchivosYRutas_SinNoStore`

## Warnings (no bloquean)
- W-01 (W-VER-03): el Block 6 no tiene fase roja; aceptado por el usuario en la ronda 1 (D-37).
- W-02 (W-VER-02): `AltaPresupuesto.cs` con 84.6 % de ramas (guardas defensivas sin alcanzar).
- W-03 (W-VER-03): las e2e de AC-10 y AC-09 dependen del orden del archivo (intencional, D-22).
- W-04: la e2e de AC-19 usa la fecha local del proceso de Playwright (misma PC que la Api).
- W-06 (datos personales): `DbUpdateException.Entries` conserva la fila del cliente en memoria (D-30).
- W-07 (datos personales): base y backups sin cifrar, riesgo R-01 aceptado por el usuario (D-27).
- W-08: importes "1.850,00" con "($)" en el encabezado (D-34) y número JSON en la Api (D-33), aprobados por el usuario.
- W-05 de la ronda 1 quedó corregido por el Block 7 (R-08 mitigado).

Result: PASSED
