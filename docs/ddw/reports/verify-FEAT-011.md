# Verification FEAT-011

Actualización de costos por planilla Excel.

**Alcance de la corrida:**
- Ronda 1 de VERIFY, sobre el commit del cierre de CODE `8612544`.
- Rama `feat/FEAT-011-costos-planilla-excel`, base `690e7f1`.

**Quién verificó y contra qué:** la verificación cruzada la hizo ddw-module-verifier, un agente que no escribió el
código. Comparó contra:
- `docs/ddw/prd/prd-FEAT-011.md`;
- `docs/ddw/specs/spec-FEAT-011.md`;
- `docs/ddw/specs/decisions-FEAT-011.md` (D-01 a D-29);
- `docs/ddw/security/sast-FEAT-011.md`;
- la corrida `docs/ddw/reports/tests-FEAT-011.md`, con 4705/4705.

**Lo que el verificador volvió a correr:**
- .NET con cobertura: 2928/2928.
- Web con cobertura: 1395/1395.
- oxlint, `tsc --noEmit` y `test:lint` (8/8).
- e2e de unidad: 164/164.

El harness Playwright (218/218) no se volvió a correr: el dato sale del informe de pruebas.

| Field | Value |
|---|---|
| Module | `src/Optica.Domain/Catalogo` (PlanillaCostos, ResultadoCostoNuevo, Articulo.ConCostoNuevo, PoliticaArticulo), `src/Optica.Application/Catalogo` (ActualizarCostosPorPlanilla, CandadoCargaPlanilla, FilasPlanillaCostos, ILectorPlanillaCostos), `src/Optica.Infrastructure` (Planillas/LectorPlanillaCostosXlsx, Persistencia/ArticuloRepository.RegistrarCostoActualizado), `src/Optica.Api` (PlanillaCostosController, limitador en Program), `web/src` (ActualizarCostos, cliente, App, Articulos), `e2e` (actualizar-costos, xlsx-minimo, backup-aviso, ci.mjs) |
| Line coverage | 95.9% |
| Branch coverage | 92.3% |
| Function coverage | 99.5% |
| Coverage floor | 80% (method default — .ddw/rules/testing.instructions.md) |
| Lint | `dotnet format Optica.sln --verify-no-changes` — clean; `dotnet build` — 0 warnings; `npm run lint` (oxlint) — clean; `tsc --noEmit` — clean; `npm run build` — clean; `npm run test:lint` 8/8 |

## Cobertura

La tabla mide los 13 archivos .NET de producción del ticket, nuevos o modificados y sin migraciones. El verificador la
re-midió y coincide con el informe de pruebas.

| Alcance | Líneas | Ramas | Funciones |
|---|---|---|---|
| Archivos del ticket (13) | 95,9 % | 92,3 % | 99,5 % |
| Suite .NET completa | 96,8 % | 89,7 % | 97,8 % |
| Web | 99 % | 97,26 % | 100 % |

Lógica central, por archivo:

| Archivo | Líneas | Ramas | Funciones |
|---|---|---|---|
| `PlanillaCostos.cs` | 99,0 % | 93,5 % | 100 % |
| `Articulo.cs` | 96,8 % | 93,3 % | 100 % |
| `ArticuloRepository.cs` | 99,1 % | 94,1 % | 100 % |
| `LectorPlanillaCostosXlsx.cs` | 96,4 % | 92,6 % | 100 % |
| `ActualizarCostosPorPlanilla.cs` | 97,6 % | 82,1 % | 100 % |

Las ramas de `ActualizarCostosPorPlanilla.cs` quedan entre el 80 y el 90 % (W-VER-02, ver Warnings).

## Acceptance criteria
- ✅ AC-01 — `ConCostoNuevo_AlicuotaYMultiplo_RecalculaVenta`, `Post_PlanillaValida_200ConResultado` (relee 2420 / 3630 / versión 1); e2e `actualiza costo y venta y lista la fila inexistente` (`Articulo.cs:ConCostoNuevo`, `ActualizarCostosPorPlanilla.cs:AplicarEnTransaccionAsync`, `ArticuloRepository.cs:RegistrarCostoActualizado`)
- ✅ AC-02 — `Evaluar_CodigoInexistente_ListaRazon`, `Ejecutar_FilasValidasEInexistente_AplicaYListaRazon`, `Post_PlanillaValida_200ConResultado`, web `muestra el resultado con aplicadas, no aplicadas y para revisar`; e2e `actualiza costo y venta y lista la fila inexistente` (`PlanillaCostos.cs:EvaluarFila`)
- ✅ AC-03 — `Evaluar_PrecioVacioYTexto_PrecioInvalido`, `Leer_PrecioTextoNumeroYCientifico_TiposCorrectos`, `Ejecutar_Log_SoloCantidadesSinCodigosNiPrecios` (`PlanillaCostos.cs:EvaluarFila`, `LectorPlanillaCostosXlsx.cs:Tipar`)
- ✅ AC-04 — `Leer_TerceraColumnaConDato_MarcaColumnasDeMas`, `TieneFormatoEsperado_TerceraColumna_Rechazado`, `Ejecutar_FormatoInesperado_RechazadoSinTocarLaBase`, `Post_TerceraColumna_400FormatoNoEsperadoYNadaActualizado`, la prueba web del mensaje de formato de la Api; e2e `planilla con tercera columna rechazada sin actualizar`
- ✅ AC-05 — `TieneFormatoEsperado_EncabezadosCodigoYPrecio_Rechazado`, `Ejecutar_FormatoInesperado_RechazadoSinTocarLaBase` ("Codigo", "Precio"), la prueba web del mensaje de formato de la Api (`PlanillaCostos.cs:TieneFormatoEsperado`)
- ✅ AC-06 — `TieneFormatoEsperado_EncabezadosConMayusculasAcentosYEspacios_Acepta` (`PlanillaCostos.cs:EsEncabezado`)
- ✅ AC-07 — `Leer_ArchivoXlsYCsv_NoEsXlsxRechazado`, `Post_ArchivoCsv_400PideXlsx` (csv y xls), web; e2e `archivo csv rechazado pide xlsx`
- ✅ AC-08 — `Evaluar_PrecioNegativoYCero_AplicaYMarca`, `Post_PlanillaValida_200ConResultado` (-100 → -150 y 0 → 0), web; e2e `lista precio negativo o cero, repetidos e inactivo`
- ✅ AC-09 — `Evaluar_CodigoEnMinusculasYConEspacios_Aplica` ("abc-1") (`PlanillaCostos.cs:Evaluar`, `NormalizacionCodigo.Clave`)
- ✅ AC-10 — `Evaluar_CodigoEnMinusculasYConEspacios_Aplica` (" ABC-1 ")
- ✅ AC-11 — `Evaluar_CodigoDuplicadoConOtraCaja_AmbasRechazadas`; e2e `lista precio negativo o cero, repetidos e inactivo`
- ✅ AC-12 — `Evaluar_ArticuloInactivo_AplicaYMarcaInactivo`, `RegistrarCostoActualizado_ArticuloInactivo_SigueInactivo`, `Post_PlanillaValida_200ConResultado`, web; e2e `lista precio negativo o cero, repetidos e inactivo`
- ✅ AC-13 — `Planilla10000Filas_Catalogo10000_MenosDe120SegundosY10EnTransaccion` (1,5 s total y 1,2 s en transacción)

## Spec blocks
- ✅ Block 1 — dominio: Logic 5/5, Error handling 5/5, 14/14 pruebas requeridas; desvío D-21.
- ✅ Block 2 — lector del .xlsx: OpenXml 3.1.1 solo en Infrastructure, lock files y singleton; 12/12 pruebas requeridas y 16 más de D-20; decisiones D-20 a D-22.
- ✅ Block 3 — caso de uso y persistencia: lógica 1 a 4, log después del commit, `appsettings` con la categoría; 12/12; D-23.
- ✅ Block 4 — endpoint: contrato JSON de la spec; 11/11; D-24 y D-25.
- ✅ Block 5 — pantalla: 8/8; D-26.
- ✅ Block 6 — e2e: 5/5, proyectos `chrome-actualizar-costos` y `msedge-actualizar-costos`; D-27.

## Required tests (spec)
- Block 1: `ConCostoNuevo_AlicuotaYMultiplo_RecalculaVenta`, `TieneFormatoEsperado_EncabezadosConMayusculasAcentosYEspacios_Acepta`, `TieneFormatoEsperado_EncabezadosCodigoYPrecio_Rechazado`, `TieneFormatoEsperado_TerceraColumna_Rechazado`, `Evaluar_CodigoInexistente_ListaRazon`, `Evaluar_CodigoVacioInvalidoOLargo_CodigoInexistente`, `Evaluar_PrecioVacioYTexto_PrecioInvalido`, `Evaluar_PrecioFueraDeRangoOVentaFueraDeRango_PrecioInvalido`, `Evaluar_PrecioConTresDecimales_RedondeaAlCentavo`, `Evaluar_PrecioNegativoYCero_AplicaYMarca`, `Evaluar_CodigoEnMinusculasYConEspacios_Aplica`, `Evaluar_CodigoDuplicadoConOtraCaja_AmbasRechazadas`, `Evaluar_ArticuloInactivo_AplicaYMarcaInactivo`, `Evaluar_CostoYVentaIguales_AplicadaSinCambio`
- Block 2: `Leer_PlanillaValida_DevuelveEncabezadosYFilasTipadas`, `Leer_ArchivoXlsYCsv_NoEsXlsxRechazado`, `Leer_XmlConEntidadExterna_RechazadoSinLeerElArchivo`, `Leer_BombaZip_RechazadoSinAgotarMemoria`, `Leer_MasDe50000FilasOIndiceAlto_ExcedeLimitesRechazado`, `Leer_TerceraColumnaConDato_MarcaColumnasDeMas`, `Leer_CeldaConFormatoSinValorEnC_NoMarcaColumnasDeMas`, `Leer_PrecioTextoNumeroYCientifico_TiposCorrectos`, `Leer_CodigoNumerico_TextoInvariante`, `Leer_PrimeraHojaPorOrdenDelLibro`, `Leer_ExcepcionDelPaquete_NoEsXlsxSinMensaje`, `Api_NoReferenciaOpenXml`
- Block 3: `Ejecutar_FilasValidasEInexistente_AplicaYListaRazon`, `Ejecutar_NoEsXlsx_RechazadoSinTocarLaBase`, `Ejecutar_FormatoInesperado_RechazadoSinTocarLaBase`, `Ejecutar_ExcedeLimites_RechazadoSinTocarLaBase`, `Ejecutar_ConflictoAlConfirmar_NadaAplicado`, `Ejecutar_ErrorAlConfirmar_NoRegistraLaCarga`, `Ejecutar_Log_SoloCantidadesSinCodigosNiPrecios`, `RegistrarCostoActualizado_ArticuloInactivo_SigueInactivo`, `RegistrarCostoActualizado_VersionVieja_ConflictoAlConfirmar`, `RegistrarCostoActualizado_EntradaConOtrosCambios_Rechazado`, `Planilla10000Filas_Catalogo10000_MenosDe120SegundosY10EnTransaccion`, `EventLog_IncluyeCategoriaPlanilla`
- Block 4: `Post_PlanillaValida_200ConResultado`, `Post_PlanillaFaltante_400`, `Post_Base64Invalido_400NoEsXlsx`, `Post_ArchivoCsv_400PideXlsx`, `Post_TerceraColumna_400FormatoNoEsperadoYNadaActualizado`, `Post_ExcedeLimites_400Rechazado`, `Post_Conflicto_409Cambiado`, `Post_CuerpoDeMas_413`, `Post_Multipart_415`, `Post_SinSesion_401`, `PlanillaCostos_NoStore`
- Block 5: `muestra el resultado con aplicadas, no aplicadas y para revisar`, `sin archivo el botón queda deshabilitado (faltante)`, `archivo de más de 10 MB rechazado sin enviar`, `muestra el error de formato inválido de la Api`, `muestra el error de conflicto 409`, `un código con HTML se muestra como texto`, `Articulos tiene el enlace a Actualizar costos`, `App` con la ruta en PANTALLAS
- Block 6: `actualiza costo y venta y lista la fila inexistente`, `planilla con tercera columna rechazada sin actualizar`, `archivo csv rechazado pide xlsx`, `lista precio negativo o cero, repetidos e inactivo`, `xlsx-minimo arma un zip válido`

## Tests
- ✅ Sad-path tests: hay pruebas con entrada inválida para cada punto de entrada del ticket.
  - Endpoint:
    - planilla faltante: `{}`, null, vacía, espacios o cuerpo vacío;
    - base64 inválido, número, arreglo o JSON cortado;
    - csv y xls, tercera columna, límites de filas y de tamaño, más de 10 MB;
    - 409, 413, 415, 401 y 429.
  - Caso de uso:
    - no es .xlsx, excede límites, lectura desconocida y formato inesperado;
    - conflicto, error al confirmar y cancelación en espera.
  - Lector: 27 pruebas, con 20 rechazos o más.
  - Dominio: fila nula, código vacío, inválido o largo, precio inválido o fuera de rango, tres decimales.
  - Repositorio: sin código, versión máxima, versión vieja, entrada con otros cambios.
  - Web: sin archivo, más de 10 MB, 400, 409, 413, 429, red, archivo ilegible, 200 ilegible y sin sesión.
- ✅ F-VER-05: lint, tipos, build y format limpios.
- ✅ TDD: cada `.ddw-work/review-block-N.md` registra el rojo previo por aserción o por mutación revertida. Ver W-6.

## Warnings (no bloquean)
- W-1 (W-VER-02): `src/Optica.Application/Catalogo/ActualizarCostosPorPlanilla.cs:48` queda en 82,1 % de ramas. Faltan
  pruebas de:
  - los argumentos null del registro y del constructor;
  - la clave repetida en el catálogo (`:212`).
- W-2 (W-VER-03): `LectorPlanillaCostosXlsxTests.cs:659` y `:761` usan umbrales de tiempo (`< 1 s`), y varias pruebas
  usan umbrales de asignación que dependen del runtime. Pueden fallar con carga sin que haya una regresión.
- W-3 (W-VER-01): `ResultadoCostoNuevo.cs:41` (`EsFueraDeRango`) solo lo usan las pruebas; producción usa
  `!EsAplicado`.
- W-4: `FilasPlanillaCostos.cs` (proyección en Application porque la Api no referencia Domain) no figura en los Files de
  la spec ni en una decisión numerada. Está justificado en la revisión del Block 4.
- W-5: el lector devuelve "no es .xlsx" en los dos casos que fija el formato de Excel. La pantalla muestra "no es .xlsx"
  en lugar de "excede límites". D-20 no lo aclara. Los casos son:
  - una fila con más de 16.384 celdas;
  - un índice de fila fuera de 1 a 1.048.576.
- W-6: el rojo del Block 1 fue por `NotImplementedException` y no por aserción. En la ronda 2 se compensó con una
  mutación revertida y una prueba que falló por aserción.
- W-7: en `decisions-FEAT-011.md`, D-22 aparece antes que D-21.
- W-8 (NFR-02): la e2e en Chrome y Edge ejercita AC-01, 02, 04, 07, 08, 11 y 12.
  - Es el subconjunto que fijó la spec aprobada (Block 6).
  - AC-03, 05, 06, 09 y 10 son lógica de servidor que no depende del navegador. Se prueban en el dominio y en el caso
    de uso, y sus mensajes en Vitest.
  - AC-13 es una medición de servidor.
- W-9 (alerta de datos de la empresa y de proveedores): queda el riesgo residual H-02 (D-29).
  - Un tipo de excepción del SDK fuera de la lista traducida llegaría al Registro de eventos con su mensaje.
  - Ese mensaje podría traer el texto de una celda.
  - Los códigos y costos de proveedores viajan solo a la sesión, con `no-store`, y no van al log.

## Notas
- Las correcciones de W-1, W-3, W-4, W-5 y W-7 son menores y quedan para un ticket aparte, junto con D-29. No se
  corrige código en VERIFY.
- Quedan para la verificación manual del usuario:
  - la versión anterior de Chrome y Edge (D-02);
  - una planilla real de un proveedor exportada desde Excel o LibreOffice.
- SAST PASSED. El aviso Alto de `picomatch` en las herramientas de desarrollo de `web/` quedó aceptado por el usuario
  (D-28). F-SAST-13 se evaluó solo sobre las dependencias que se publican; el método no tiene una excepción para
  avisos Altos.

Result: PASSED
