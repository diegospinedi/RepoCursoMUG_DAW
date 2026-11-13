# Verification FEAT-006

Ronda 1 de VERIFY, sobre `ada0714` (rama `feat/FEAT-006-busqueda-pdf-presupuestos`), con un verificador independiente
(`ddw-module-verifier`, que no escribió el código). Corrió por su cuenta la suite completa, la cobertura y el harness e2e
en Chrome y Edge; `git status` vacío antes y después.

| Field | Value |
|---|---|
| Module | búsqueda y PDF de presupuestos: `src/Optica.Domain/Presupuestos/{FiltroPresupuestos,ResumenPresupuesto,PaginaPresupuestos}.cs`, `src/Optica.Infrastructure/Persistencia/PresupuestoRepository.cs`, `src/Optica.Infrastructure/Pdf/GeneradorPdfPresupuesto.cs`, `src/Optica.Application/Presupuestos/{BuscarPresupuestos,GenerarPdfPresupuesto,ValidacionPresupuesto}.cs`, `src/Optica.Api/Controllers/PresupuestosController.cs`, `web/src/paginas/{BuscarPresupuestos,Presupuesto}.tsx`, `web/src/api/cliente.ts` |
| Line coverage | 99.1% |
| Branch coverage | 94.7% |
| Function coverage | 100% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet build Optica.sln --no-incremental` — 0 warnings, 0 errors; `dotnet format Optica.sln --verify-no-changes` — clean; `npm run lint` (oxlint) — clean; `npm run build` (tsc + vite) — clean; `npm run test:lint` — 8/8 |

Cobertura de la tabla: archivos de producción .NET del ticket, unión de los cuatro informes de coverlet por ruta
completa, sin migraciones: líneas 1092/1102, ramas 446/471, funciones 160/160 (con `Program.cs`: 97,2/94,4/100). Total
.NET 97,0/93,7/96,7. Web 99,19 % de líneas, 96,94 % de ramas, 100 % de funciones (`BuscarPresupuestos.tsx` 100 %,
`cliente.ts` 100/99,57, `Presupuesto.tsx` 98,43/94,56). Corridas del verificador: .NET 1789/1789, web 903/903, e2e
unidad 71/71, e2e 144/144 (los 10 de FEAT-006 en Chrome y Edge; la grilla en 556 ms y 589 ms).

## Acceptance criteria
- ✅ AC-01 — `FiltroPresupuestos.Crear` + `PresupuestoRepository.BuscarAsync` (`instr(plegar)`) → `Buscar_ApellidoParcialSinAcentos_SoloGonzalez`, `PostBusqueda_GonzalezYGomez_SoloGonzalez`, e2e `búsqueda por apellido, nombre, DNI y fechas`
- ✅ AC-02 — mismo camino con Nombre → `Buscar_NombreSinAcento_SoloMariaJose`, e2e
- ✅ AC-03 — DNI sin puntos ni guiones e `instr(Dni)` → `Buscar_DniParcialConPuntos_Coincide`, `Filtro_DniConPuntosYGuiones_SoloDigitos`, e2e
- ✅ AC-04 — filtros combinados con "Y" → `Buscar_ApellidoYDesde_CombinaConY`, e2e (ayer, hoy y mañana, D-40)
- ✅ AC-05 — límites inclusive → `Buscar_RangoDesdeHasta_LimitesInclusive`, `Filtro_DesdeIgualAHasta_Valido`
- ✅ AC-06 — solo Hasta → `Buscar_SoloHasta_IncluyeEseDiaYAnteriores`
- ✅ AC-07 — `ValidacionPresupuesto.ValidarBusqueda`, `reglas.validarRangoFechas` → `Buscar_DesdePosteriorAHasta_Rechazado`, `Filtro_DesdePosteriorAHasta_Rechazado`, `desde posterior a hasta: rechazado sin llamar a la Api`, e2e
- ✅ AC-08 — orden descendente y páginas de 50 → `Buscar_SinFiltros_120_PaginasDe50Descendente`, `Buscar_SinFiltros_Pagina1De50`, `buscar sin filtros muestra la primera página y Siguiente`, e2e `paginación y abrir un resultado`
- ✅ AC-09 — siete datos por resultado → `Buscar_ResultadoTraeLosSieteDatos`, `Buscar_TotalEsSumaDePreciosFinales`, `cada resultado muestra los siete datos`
- ✅ AC-10 — enlace al presupuesto → `el número abre el presupuesto`, e2e `paginación y abrir un resultado`
- ✅ AC-11 — `PresupuestosController.DescargarPdf`, `GenerarPdfPresupuesto`, `GeneradorPdfPresupuesto.ArmarPagina`, `Presupuesto.tsx descargarPdf` → `Pdf_LogoYColoresDeLaMarca`, `Pdf_PresupuestoFinal_DevuelvePdf`, `Descargar PDF habilitado en Final y descarga presupuesto-N.pdf`, e2e `descarga el PDF de un Final con el número`
- ✅ AC-12 — Borrador sin generar, 409 `borrador`, botón `disabled` → `Pdf_PresupuestoBorrador_ConflictoSinGenerar`, `GetPdf_Borrador_409ConflictoBorrador`, `Pdf_PresupuestoEnBorrador_Rechazado`, `Descargar PDF deshabilitado en Borrador`, e2e `Borrador: botón deshabilitado y la Api rechaza el pedido directo`
- ✅ AC-13 — `GeneradorPdfPresupuesto.DibujarCliente` → `Pdf_ClienteSinDomicilioNiEmail_SoloCuatroDatos`, `GetPdf_Final_ContenidoDeAC13AC14AC15`
- ✅ AC-14 — `DibujarLineas` con importes grabados y formato fijo → `Pdf_LineasConSieteCamposYTotal`, `Pdf_DescuentoConDecimales_SinCerosALaDerecha`
- ✅ AC-15 — leyenda sin IVA discriminado → `Pdf_LeyendaIvaIncluido_SinIvaDiscriminado`
- ✅ AC-16 — página por la clave primaria y SUM en SQL → `Volumen_20BusquedasPorApellido_19BajoDosSegundos` (10.000 sembrados, 6,6 a 68,4 ms; D-03, D-26), e2e `buscar muestra la grilla en menos de 2 s`
- ✅ AC-17 — los e2e de FEAT-006 en los proyectos chrome-* y msedge-*: 10/10; el contenido del PDF y el volumen se verifican en .NET por D-02 y D-03 (decisiones del usuario)

## Spec blocks
- ✅ Block 1 — filtro y consulta de la búsqueda: 21/21 pruebas requeridas, tres dobles actualizados
- ✅ Block 2 — caso de uso y endpoint de la búsqueda: 14/14
- ✅ Block 3 — pantalla de búsqueda: 10/10
- ✅ Block 4 — generador del PDF: 12/12 requeridas (más 9 adicionales)
- ✅ Block 5 — caso de uso y endpoint del PDF: 10/10
- ✅ Block 6 — botón Descargar PDF y e2e: 11/11

Evidencia TDD: las revisiones `.ddw-work/review-block-1..6.md` registran pruebas en rojo antes de implementar en cada
bloque y en las rondas de corrección; todas las pruebas citadas existen en disco. La fase roja no se reproduce desde git
porque cada commit trae pruebas y código juntos.

## Tests
- ✅ Sad-path tests: búsqueda (`PostBusqueda_CuerpoIlegible_400`, `Buscar_FechaInvalida_Rechazada`, `Buscar_DesdePosteriorAHasta_Rechazado`, `Buscar_PaginaInvalida_Rechazada`, `Buscar_PaginaFueraDeTope_Rechazada`, `Buscar_ApellidoDemasiadoLargo_Rechazado`, `Buscar_TextoConUtf16Invalido_Rechazado`, `PostBusqueda_SinSesion_401NoAutorizado`, `PostBusqueda_DemasiadoGrande_413`, `PostBusqueda_SinJson_415`, `Buscar_PorcentajeYGuionBajo_NoSonComodines`); PDF (`Pdf_PresupuestoInexistente_NoEncontrado404`, `GetPdf_Borrador_409ConflictoBorrador`, `GetPdf_SinSesion_401NoAutorizado`, `GetPdf_GeneradorConError_500Generico_LogSinDatosDelCliente`, `Pdf_LogoIlegible_ErrorSinRuta`); pantallas (400, 413, red, 500 y 401)
- ✅ Todas las pruebas nombradas en la spec existen con su nombre y pasan (58 de .NET, 17 de web y e2e), por bloque:
- ✅ Block 1: `Filtro_ApellidoConAcentosYMayusculas_SePliega`; `Filtro_DniConPuntosYGuiones_SoloDigitos`; `Filtro_TextosVacios_NoCargados`; `Filtro_DesdePosteriorAHasta_Rechazado`; `Filtro_DesdeIgualAHasta_Valido`; `PaginaPresupuestos_TotalInvalido_Rechazado`; `Buscar_ApellidoParcialSinAcentos_SoloGonzalez`; `Buscar_NombreSinAcento_SoloMariaJose`; `Buscar_DniParcialConPuntos_Coincide`; `Buscar_ApellidoYDesde_CombinaConY`; `Buscar_RangoDesdeHasta_LimitesInclusive`; `Buscar_SoloHasta_IncluyeEseDiaYAnteriores`; `Buscar_SinFiltros_120_PaginasDe50Descendente`; `Buscar_TotalEsSumaDePreciosFinales`; `Buscar_PorcentajeYGuionBajo_NoSonComodines`; `Buscar_DniConLetras_SinCoincidencias`; `Buscar_PaginaInvalida_Rechazada`; `Buscar_PaginaQueDesborda_Rechazada`; `Buscar_TotalMenorQueLaPagina_SeAjusta`; `Filtro_ToString_SinDatosDelCliente`; `Resumen_ToString_SinDatosDelCliente`; `Buscar_FechaGuardadaIlegible_ErrorSinValores`
- ✅ Block 2: `Buscar_SinFiltros_Pagina1De50`; `Buscar_ResultadoTraeLosSieteDatos`; `Buscar_FechaInvalida_Rechazada`; `Buscar_DesdePosteriorAHasta_Rechazado`; `Buscar_PaginaInvalida_Rechazada`; `Buscar_ApellidoDemasiadoLargo_Rechazado`; `PostBusqueda_CuerpoIlegible_400`; `PostBusqueda_SinSesion_401NoAutorizado`; `PostBusqueda_RespuestaNoStore`; `PostBusqueda_GonzalezYGomez_SoloGonzalez`; `Buscar_PaginaFueraDeTope_Rechazada`; `Volumen_20BusquedasPorApellido_19BajoDosSegundos`; `ResultadoBusqueda_ToString_SinValores`; `SolicitudBusqueda_ToString_SinValores`
- ✅ Block 3: `buscar sin filtros muestra la primera página y Siguiente`; `cada resultado muestra los siete datos`; `el número abre el presupuesto`; `desde posterior a hasta: rechazado sin llamar a la Api`; `error 400 inválido de la Api junto al campo`; `error de red: mensaje general`; `413 rechazado: mensaje general fijo`; `validarRangoFechas`; `buscarPresupuestos envía el cuerpo y no pone filtros en la URL`; `App: /presupuestos tiene pantalla`; `Inicio: enlace Buscar presupuestos`
- ✅ Block 4: `Pdf_LogoYColoresDeLaMarca`; `Pdf_LogoPng_SeDibuja`; `Pdf_LogoJpeg_SeDibuja`; `Pdf_LogoSvg_SeDibuja`; `Pdf_ClienteConGlifoFueraDeLaFuente_SeGeneraConReemplazo`; `Pdf_FuenteIncluidaEnLaSalida`; `Pdf_ClienteSinDomicilioNiEmail_SoloCuatroDatos`; `Pdf_LineasConSieteCamposYTotal`; `Pdf_LeyendaIvaIncluido_SinIvaDiscriminado`; `Pdf_MetadatosSinDatosDelCliente`; `Pdf_PresupuestoEnBorrador_Rechazado`; `Pdf_PresupuestoNulo_Rechazado`; `Pdf_LogoIlegible_ErrorSinRuta`
- ✅ Block 5: `Pdf_PresupuestoFinal_DevuelvePdf`; `GetPdf_Final_ContenidoDeAC13AC14AC15`; `Pdf_PresupuestoBorrador_ConflictoSinGenerar`; `GetPdf_Borrador_409ConflictoBorrador`; `Pdf_PresupuestoInexistente_NoEncontrado404`; `GetPdf_SinSesion_401NoAutorizado`; `GetPdf_GeneradorConError_500Generico_LogSinDatosDelCliente`; `Api_NoReferenciaQuestPdf`; `GetPdf_RespuestaNoStore`; `ConflictoPresupuesto_MotivoBorrador_Aceptado`
- ✅ Block 6: `Descargar PDF habilitado en Final y descarga presupuesto-N.pdf`; `Descargar PDF deshabilitado en Borrador`; `409 borrador: conflicto con mensaje propio y relectura`; `error de red al descargar: mensaje fijo`; `401 al descargar: no autorizado, va a ingreso`; `cliente: 409 con motivo borrador se lee como conflicto`; e2e `búsqueda por apellido, nombre, DNI y fechas`; e2e `paginación y abrir un resultado`; e2e `buscar muestra la grilla en menos de 2 s`; e2e `descarga el PDF de un Final con el número`; e2e `Borrador: botón deshabilitado y la Api rechaza el pedido directo`
- ✅ Sin pruebas frágiles: el e2e no usa esperas fijas y el volumen se mide después de un calentamiento

## AGENTS.md y datos personales
- ✅ Sin migraciones ni cambios en los triggers de `CierrePresupuestos`; ningún camino modifica un presupuesto en Final; sin ampliación de alcance (D-41); datos de prueba grabados en Borrador y pasados a Final.
- ✅ Ley 25.326: búsqueda por POST sin datos en la URL; nada en el log (verificado en Trace, incluido el 500 del PDF); nombre de archivo y metadatos del PDF solo con el número; errores con texto fijo; nada en el almacenamiento del navegador.
- Riesgos residuales aceptados, no defectos: R-08 (el PDF descargado queda en Descargas, avisado en `docs/instalacion.md`, D-38), R-09 (base sin cifrar), D-31 (`autoComplete="off"` de máximo esfuerzo).

## Warnings
- ⚠️ W-VER-02 — `src/Optica.Application/Presupuestos/BuscarPresupuestos.cs:67-69` y `:79-81`: los resguardos de `ResultadoBusquedaPresupuestos.Valido` (página menor que 1) e `Invalido` (errores nulos o vacíos) no tienen prueba; el archivo queda en 5/8 ramas (62,5 %) y 49/53 líneas, con el ticket en 94,7 %. Además corrige `docs/ddw/reports/tests-FEAT-006.md`, que atribuía esas ramas a "los `??` de lectura de fechas": no existen; son estos resguardos.
- ℹ️ Corrección del informe de pruebas: `ResumenPresupuesto.cs` sí está instrumentado (22/22 líneas, 8/8 métodos); el "sin datos" venía del script de unión, que dividía por cero en un archivo sin ramas. Las cifras del ticket no cambian.
- ℹ️ `ConfiguracionInfraestructura.cs` 2/4 ramas: ramas de FEAT-001 que dependen del sistema operativo; la línea nueva está cubierta.
- ℹ️ `PresupuestoRepository.cs:311`: la rama de estado desconocido es inalcanzable por `CK_Presupuesto_Estado`.
- ℹ️ D-25 sigue abierto (cuerpo vacío: 200 con `Content-Length: 0`, 400 en partes), sin datos personales; para decidir en otro ticket.
- ℹ️ D-37: incidente de `sed -i` sobre un archivo de prueba en el Block 5, validado sin impacto.

Result: PASSED
