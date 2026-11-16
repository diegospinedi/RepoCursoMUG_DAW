# Verification FEAT-009

PDF, listado y búsqueda de facturas. Ronda 1 de VERIFY, sobre el commit del cierre de CODE (rama
`feat/FEAT-009-facturas-pdf-listado`, base `5c47270`). Verificación cruzada de ddw-module-verifier (agente que no
escribió el código) contra `docs/ddw/prd/prd-FEAT-009.md`, `docs/ddw/specs/spec-FEAT-009.md`,
`docs/ddw/specs/decisions-FEAT-009.md` y la corrida `docs/ddw/reports/tests-FEAT-009.md` (4129/4129). El verificador
volvió a correr pruebas filtradas: Domain 32/32, Application 28/28, Infrastructure 141/141, Api 194/194, Vitest de
los 8 archivos del ticket 741/741, e2e unidad 108/108 y oxlint limpio.

| Field | Value |
|---|---|
| Module | `src/Optica.Domain/Configuracion` (Cuit, DatosEmisor), `src/Optica.Domain/Facturacion` (ReglasComprobanteImpreso, FiltroFacturas), `src/Optica.Application/Configuracion` y `Facturacion` (QrArca, DocumentoFactura, ObtenerFactura, GenerarPdfFactura, BuscarFacturas), `src/Optica.Infrastructure/Pdf/GeneradorPdfFactura.cs`, `Persistencia` (DatosEmisorRepository, FacturaRepository, FuncionesSqlite), `src/Optica.Api/Controllers`, `web/src/paginas` (SeccionDatosEmisor, BuscarFacturas, DetalleFactura), `e2e/tests` (configuracion-emisor, facturas-pdf, facturas-busqueda) |
| Line coverage | 96.8% |
| Branch coverage | 87.6% |
| Function coverage | 97.8% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean; `dotnet build` — 0 warnings; `npm run lint` (oxlint) — clean; `npm run build` (tsc) — clean; `npm run test:lint` 8/8 |

Cobertura de la tabla: los 33 archivos .NET de producción del ticket (nuevos o modificados, sin migraciones), unión de
los cuatro informes de coverlet del cierre de CODE: líneas 2997/3096, ramas 790/902, funciones 491/502. Suite
completa: .NET 96,9 / 89,6 / 97,4; web 98,95 / 97,06 / 100 / 98,90.

## Acceptance criteria
- ✅ AC-01 — `Repositorio_GrabaYLeeLosCincoDatos`, `Grabar_CincoDatos_ReemplazaYRegistraSinValores`, `Emisor_GrabaCincoDatos_LosMuestraAlVolver`; e2e `configuracion-emisor.spec.ts:226`
- ✅ AC-02 — `DatosEmisor_SinRazonSocial_Rechazado`, `Grabar_SinRazonSocial_RechazadoSinGrabar`, `Emisor_SinRazonSocial_ErrorJuntoAlCampo`; e2e `:165`
- ✅ AC-03 — `Cuit_ConGuiones_SeGuardaSinGuiones` (`Cuit.cs:37`), `PostEmisor_CuitConGuiones_200SinGuiones`, `Reglas_Cuit_DigitoVerificador`; e2e `:209`
- ✅ AC-04 — `Cuit_DigitoVerificadorInvalido_Rechazado`, `Cuit_LargoInvalido_Rechazado`, `PostEmisor_CuitInvalido_400JuntoAlCampo`, `Emisor_CuitInvalido_ErrorJuntoAlCampo`; e2e `:185`
- ✅ AC-05 — `GenerarPdf_SinRazonSocial_SinDatosEmisor`, `GetFacturaPdf_SinDatosEmisor_409`, `Detalle_SinDatosEmisor_AvisoConEnlace`; e2e `facturas-pdf.spec.ts:363`
- ✅ AC-06 — `Pdf_LogoYColores` (`GeneradorPdfFactura.cs:99`), `GetFacturaPdf_Descarga_NombreSinDatos`, `Detalle_DescargarPdf`; e2e `:409`
- ✅ AC-07 — `Pdf_EmisorYCondicionResponsableInscripto`, `ReglasComprobanteImpreso_CondicionYValidezFiscal` (`ReglasComprobanteImpreso.cs:23`); e2e `:409`
- ✅ AC-08 — `Pdf_ComprobanteYFecha_B000300000125` (fecha fija 30/10/2026); e2e `:409` con la fecha del día (D-45)
- ✅ AC-09 — `Pdf_ConsumidorFinalSinDni`, `QrArca_ConsumidorFinal_TipoDoc99SinDni`; e2e `:454`
- ✅ AC-10 — `Pdf_ReceptorConDniApellidoYNombre` (sin datos del receptor en los metadatos); e2e `:409`
- ✅ AC-11 — `Pdf_LineasYTotal_271002`; e2e `:409`
- ⚠️ AC-12 — `Pdf_CaeVencimientoEImagenQr`, `QrArca_FacturaBConDniEnProduccion_UrlYJsonDeArca`, `QrArca_HostYRutaConstantes` (contenido del QR decodificado y verificado); el escaneo con un teléfono queda como verificación manual del usuario (D-16)
- ✅ AC-13 — `Pdf_FacturaB_LeyendaLey27743Con31500`; e2e `:454`
- ✅ AC-14 — `Pdf_FacturaC_SinIvaContenido`; e2e `:501`
- ✅ AC-15 — `Pdf_SimuladoYHomologacion_LeyendaEnCadaPagina` (dos páginas, `GeneradorPdfFactura.cs:108`); e2e de 70 líneas `:484`
- ⚠️ AC-16 — `Pdf_Produccion_SinLeyenda`, `ReglasComprobanteImpreso_CondicionYValidezFiscal`; probado en .NET y no en los navegadores, porque el harness siempre corre en Simulado y AGENTS.md prohíbe emitir contra producción (D-45)
- ✅ AC-17 — `Repositorio_120Facturas_Paginas50y20`, `Repositorio_EmpatesDeFechaYNumero_SinRepetirNiPerderEntrePaginas` (`FacturaRepository.cs:207-211`); e2e `facturas-busqueda.spec.ts:440`
- ✅ AC-18 — `Repositorio_ApellidoYDesde_SoloGonzalez20` (fechas de marzo); e2e `:338` con la fecha del día
- ✅ AC-19 — `Repositorio_Numero34561`, `Repositorio_PuntoDeVentaDeCincoDigitos_PorNumero`, `Filtro_DniYNumero_SinPuntosNiGuiones`; e2e `:338`
- ✅ AC-20 — `Repositorio_Dni3456y00345`; e2e `:338`
- ✅ AC-21 — `Repositorio_NombreMaria_SoloMariaJose`; e2e `:338`
- ✅ AC-22 — `Filtro_DesdePosteriorAHasta_Rechazado`, `PostBusqueda_DesdePosterior_400JuntoADesde`, `Buscar_DesdePosterior_ErrorSinPedido`; e2e `:338` sin pedido
- ✅ AC-23 — `PostBusqueda_Resultado_ColumnasDeAC23`, `Buscar_Resultado_ColumnasDeAC23`; e2e `:270`
- ✅ AC-24 — `GetFactura_Detalle_Presupuesto155SinAcciones` (`PresupuestosController.cs:605`), `ObtenerFactura_ConsumidorFinal_ClienteDelPresupuesto`, `Detalle_Presupuesto155_SinAccionesDeEdicion`; e2e `:270`
- ✅ AC-25 — los tres archivos e2e en los proyectos `chrome-*` y `msedge-*` (99 + 99 en el cierre); la versión anterior de los navegadores queda como verificación manual (D-02)
- ✅ AC-26 — `Repositorio_ConsumidorFinal_PorDniDelCliente`, `ObtenerFactura_ConsumidorFinal_ClienteDelPresupuesto`; e2e `:411`
- ✅ AC-27 — `Pdf_FacturaCCondicionMonotributo`, `ReglasComprobanteImpreso_CondicionYValidezFiscal`; e2e `:501`

## Spec blocks
- ✅ Block 1 — 10/10 archivos, 14/14 pruebas con el nombre de la spec
- ✅ Block 2 — 7/7 archivos, 9/9 pruebas
- ✅ Block 3 — 4/4 archivos, 5/5 pruebas
- ✅ Block 4 — 12/12 archivos, 23/23 pruebas
- ✅ Block 5 — 6/6 archivos, 10/10 pruebas
- ✅ Block 6 — 14/14 archivos, 16/16 pruebas
- ✅ Block 7 — 7/7 archivos, 8/8 pruebas
- ✅ Block 8 — 5/5 archivos, 5/5 pruebas (más los casos de contador inválido de `compilacion.test.mjs`)

Evidencia TDD por bloque en las revisiones (`.ddw-work/review-block-1.md` a `review-block-8.md`), con dos rondas en
cada bloque y los FAIL de arquitectura de los Blocks 3 y 7 cerrados en la ronda 2.

## Required tests (spec)

Block 1:
- ✅ `Cuit_ConGuiones_SeGuardaSinGuiones`
- ✅ `Cuit_DigitoVerificadorInvalido_Rechazado`
- ✅ `Cuit_LargoInvalido_Rechazado`
- ✅ `DatosEmisor_SinRazonSocial_Rechazado`
- ✅ `DatosEmisor_InicioFuturo_Rechazado`
- ✅ `DatosEmisor_TextoLargo_Rechazado`
- ✅ `Repositorio_GrabaYLeeLosCincoDatos`
- ✅ `Repositorio_SinFila_DevuelveNull`
- ✅ `Repositorio_FilaCorrupta_ErrorSinValores`
- ✅ `Migracion_DatosEmisor_ConservaTodosLosTriggers`
- ✅ `Migracion_Down_Revierte`
- ✅ `OpcionesArca_CuitInvalido_SigueRechazado`
- ✅ `OpcionesArca_CuitConGuiones_SigueRechazado`
- ✅ `DatosEmisor_LeerConInicioFuturo_NoEsCorrupta`

Block 2:
- ✅ `Grabar_CincoDatos_ReemplazaYRegistraSinValores`
- ✅ `Grabar_SinRazonSocial_RechazadoSinGrabar`
- ✅ `PostEmisor_CuitConGuiones_200SinGuiones`
- ✅ `PostEmisor_CuitInvalido_400JuntoAlCampo`
- ✅ `PostEmisor_CuerpoIlegible_400`
- ✅ `PostEmisor_CuerpoGrande_413`
- ✅ `RutasEmisor_SinSesion_401`
- ✅ `GetEmisor_SinDatos_200ConNulls`
- ✅ `RutasEmisor_NoStore`

Block 3:
- ✅ `Emisor_GrabaCincoDatos_LosMuestraAlVolver`
- ✅ `Emisor_SinRazonSocial_ErrorJuntoAlCampo`
- ✅ `Emisor_CuitInvalido_ErrorJuntoAlCampo`
- ✅ `Emisor_ErrorDelServidor_MensajeGeneral`
- ✅ `Reglas_Cuit_DigitoVerificador`

Block 4:
- ✅ `QrArca_FacturaBConDniEnProduccion_UrlYJsonDeArca`
- ✅ `QrArca_ConsumidorFinal_TipoDoc99SinDni`
- ✅ `QrArca_FueraDeProduccion_SinDni`
- ✅ `QrArca_HostYRutaConstantes`
- ✅ `QrArca_CodigosCoincidenConCodigosWsfe`
- ✅ `ReglasComprobanteImpreso_CondicionYValidezFiscal`
- ✅ `Pdf_FallaInterna_ErrorConTextoFijo`
- ✅ `Documento_CaeInvalido_Rechazado`
- ✅ `Documento_EmisorFaltante_Rechazado`
- ✅ `Pdf_LogoYColores`
- ✅ `Pdf_EmisorYCondicionResponsableInscripto`
- ✅ `Pdf_FacturaCCondicionMonotributo`
- ✅ `Pdf_ComprobanteYFecha_B000300000125`
- ✅ `Pdf_ConsumidorFinalSinDni`
- ✅ `Pdf_ReceptorConDniApellidoYNombre`
- ✅ `Pdf_LineasYTotal_271002`
- ✅ `Pdf_CaeVencimientoEImagenQr`
- ✅ `Pdf_FacturaB_LeyendaLey27743Con31500`
- ✅ `Pdf_FacturaC_SinIvaContenido`
- ✅ `Pdf_SimuladoYHomologacion_LeyendaEnCadaPagina`
- ✅ `Pdf_Produccion_SinLeyenda`
- ✅ `Pdf_LogoInvalido_SeGeneraSinLogo`
- ✅ `Api_NoReferenciaQrCoder`

Block 5:
- ✅ `ObtenerFactura_ConsumidorFinal_ClienteDelPresupuesto`
- ✅ `GenerarPdf_SinRazonSocial_SinDatosEmisor`
- ✅ `GenerarPdf_Inexistente_NoEncontrado`
- ✅ `GetFacturaPdf_Descarga_NombreSinDatos`
- ✅ `GetFacturaPdf_SinDatosEmisor_409`
- ✅ `GetFactura_Detalle_Presupuesto155SinAcciones`
- ✅ `GetFactura_SinFactura_404`
- ✅ `GetFacturaPdf_GeneradorLanza_500SinTexto`
- ✅ `RutasFactura_SinSesion_401`
- ✅ `RutasFactura_NoStore`

Block 6:
- ✅ `Filtro_DesdePosteriorAHasta_Rechazado`
- ✅ `Filtro_DniYNumero_SinPuntosNiGuiones`
- ✅ `Repositorio_120Facturas_Paginas50y20`
- ✅ `Repositorio_EmpatesDeFechaYNumero_SinRepetirNiPerderEntrePaginas`
- ✅ `Repositorio_PuntoDeVentaDeCincoDigitos_PorNumero`
- ✅ `Repositorio_ApellidoYDesde_SoloGonzalez20`
- ✅ `Repositorio_Numero34561`
- ✅ `Repositorio_Dni3456y00345`
- ✅ `Repositorio_NombreMaria_SoloMariaJose`
- ✅ `Repositorio_ConsumidorFinal_PorDniDelCliente`
- ✅ `PostBusqueda_Resultado_ColumnasDeAC23`
- ✅ `PostBusqueda_DesdePosterior_400JuntoADesde`
- ✅ `PostBusqueda_CuerpoIlegible_400`
- ✅ `PostBusqueda_CuerpoGrande_413`
- ✅ `PostBusqueda_SinSesion_401`
- ✅ `PostBusqueda_Registro_SinDatosPersonales`

Block 7:
- ✅ `Buscar_Resultado_ColumnasDeAC23`
- ✅ `Buscar_DesdePosterior_ErrorSinPedido`
- ✅ `Buscar_FiltrosFueraDeLaUrl`
- ✅ `Detalle_Presupuesto155_SinAccionesDeEdicion`
- ✅ `Detalle_DescargarPdf`
- ✅ `Detalle_SinDatosEmisor_AvisoConEnlace`
- ✅ `Detalle_RespuestaIlegible_MensajeGeneral`
- ✅ `Pantallas_NingunaMuestraDatosArca_IncluyeFacturas`

Block 8:
- ✅ `los`

## Tests
- ✅ Sad-path tests: los cinco endpoints nuevos con 400 por campo, cuerpo ilegible, 413, 415, 401, 404, 409 `sin_datos_emisor` y 500 sin texto (`PostEmisor_CuitInvalido_400JuntoAlCampo`, `PostEmisor_CuerpoIlegible_400`, `PostEmisor_CuerpoGrande_413`, `PostEmisor_SinJson_415`, `RutasEmisor_SinSesion_401`, `Emisor_FilaCorrupta_Error500Generico`, `GetFactura_SinFactura_404`, `GetFacturaPdf_SinDatosEmisor_409`, `GetFacturaPdf_GeneradorLanza_500SinTexto`, `PostBusqueda_DesdePosterior_400JuntoADesde`, `PostBusqueda_TextoLargoYRangoInvertido_SoloErrorDelCampo`, `PostBusqueda_CuerpoIlegible_400`, `PostBusqueda_CuerpoGrande_413`, `PostBusqueda_SinSesion_401`); casos de uso con entrada inválida o falla (`Grabar_FallaAlLeerOAlConfirmar_NoRegistraNiGraba`, `GenerarPdf_Inexistente_NoEncontrado`, `Documento_CaeInvalido_Rechazado`, `Pdf_FallaInterna_ErrorConTextoFijo`, `ErrorDeTexto_RecorteDelMoldeYUtf16Invalido`)
- ✅ Spec tests: las 86 pruebas listadas en la spec existen y pasan, sin renombres
- ✅ Lint / type checker: limpio (tabla de arriba)
- ⚠️ W-VER-02: ramas de la lógica de negocio del ticket en 87,6 %; `DocumentoFactura.cs` 41/56 (guardas de construcción), `BuscarFacturas.cs` 9/12, `PaginaFacturas.cs` 2/4
- ⚠️ W-VER-01: tres exportaciones web usadas solo dentro de su archivo (`FORMATO_FECHA` en `web/src/api/fechas.ts:7`, `LEYENDA_SIMULADA` en `DetalleFactura.tsx:14`, `MENSAJE_SIN_FACTURAS` en `BuscarFacturas.tsx:20`), como el molde de `BuscarPresupuestos.tsx`
- ⚠️ W-VER-03: los e2e de búsqueda dependen de la fecha del día y no admiten cruzar la medianoche; fallan con un mensaje explícito (`facturas-busqueda.spec.ts:350-357`, `:470-475`)
- ⚠️ W-VER-03: los tres e2e nuevos corren en modo serial con estado compartido dentro de cada archivo (convención del harness: una base por archivo, workers 1, retries 0)

## Otras observaciones
- ⚠️ Orden del listado (FR-22, D-20): fecha, número e Id descendentes; si en un mismo día conviven Facturas B y C (solo
  si cambia la condición fiscal), se ordenan por número entre tipos distintos.
- ⚠️ Datos personales heredados: el DNI 30111222 de pruebas anteriores al ticket (`SinDatosArcaTests.cs:52`,
  `ProteccionTests.cs:602`, `SinCacheTests.cs:515`) podría ser de una persona real; reemplazarlo en otro ticket.
- Pendientes manuales y de producción: escaneo del QR (D-16), versión anterior de los navegadores (D-02), CUIT del QR
  igual a `Arca:Cuit` (D-17), leyenda de la Ley 27.743 confirmada por el contador (D-04), marca real (P-01).

## Alcance, registros cerrados y datos personales
- ✅ Sin ampliación de alcance; lo agregado fuera de Files es técnico y está registrado (D-35, D-37, D-42, D-43, D-45)
- ✅ Sin rutas de escritura sobre facturas ni presupuestos Final; la migración `DatosEmisor` solo crea y borra su tabla;
  `Migracion_DatosEmisor_ConservaTodosLosTriggers` compara el conjunto completo de triggers
- ✅ DNI del cliente en búsqueda y detalle por FR-25 (decisión del usuario), con sesión, `no-store` y sin log; DNI en el
  PDF solo con receptor identificado y en el QR solo en producción; datos del emisor sin valores en el log; carpeta
  `Marca/` fuera del repositorio

Total: 10 advertencias que no bloquean y ningún ítem rojo.

Result: PASSED
