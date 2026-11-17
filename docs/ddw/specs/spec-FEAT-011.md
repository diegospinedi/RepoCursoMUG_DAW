# Spec FEAT-011: Actualización de costos por planilla Excel

| Field | Value |
|-------|-------|
| Ticket | FEAT-011 |
| PRD | docs/ddw/prd/prd-FEAT-011.md |
| Tier | FEATURE |
| Date | 2026-11-17 |
| Spec loops | 1 |
| Loops since last human decision | 1 |

## Summary
La pantalla "Actualizar costos" envía la planilla .xlsx en base64 dentro de un JSON a
`POST /api/articulos/planilla-costos` (D-06, ADR-031). `LectorPlanillaCostosXlsx` (Infrastructure, OpenXml 3.1.1, SAX,
con límites) devuelve la fila 1 y las filas como celdas tipadas, o "no es .xlsx". `PlanillaCostos` (dominio puro)
decide el formato, el precio, los repetidos y el resultado por fila con la clave de ADR-009, y
`Articulo.ConCostoNuevo` recalcula el precio de venta. El caso de uso `ActualizarCostosPorPlanilla` aplica todo en una
transacción con una sola confirmación, registra cantidades en el log y devuelve el resultado que la pantalla muestra.
Decisiones D-01 a D-19 en `docs/ddw/specs/decisions-FEAT-011.md`; amenazas R-01 a R-10 en
`docs/ddw/security/threat-FEAT-011.md`.

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 3, Block 4, Block 5 |
| FR-02 | Block 2, Block 4, Block 5 |
| FR-03 | Block 1 |
| FR-04 | Block 1, Block 3 |
| FR-05 | Block 1, Block 4, Block 5 |
| FR-06 | Block 1, Block 3 |
| FR-07 | Block 1 |
| FR-08 | Block 1, Block 2 |
| FR-09 | Block 1 |
| FR-10 | Block 1 |
| FR-11 | Block 1, Block 3 |
| FR-12 | Block 1, Block 3 |
| FR-13 | Block 1, Block 4, Block 5 |
| FR-14 | Block 1, Block 4, Block 5 |
| FR-15 | Block 1, Block 4, Block 5 |
| NFR-01 | Strategy: una lectura del catálogo, diccionario por clave, `AutoDetectChanges` apagado y una confirmación; prueba de rendimiento con 10.000 artículos y 10.000 filas sobre SQLite real (Block 3), umbral 120 s total y 10 s dentro de la transacción |
| NFR-02 | Strategy: e2e en Chrome y Edge estables (Block 6, D-02) |

## Dependencies between blocks
Block 1 no depende de nada. Block 2 depende de Block 1 (tipos de celda y fila del dominio). Block 3 depende de 1 y 2.
Block 4 depende de 3. Block 5 depende de 4 (contrato). Block 6 depende de 4 y 5. Orden: 1 → 2 → 3 → 4 → 5 → 6.

## Block 1 — Reglas de la planilla en el dominio

**Files**
- `src/Optica.Domain/Catalogo/PlanillaCostos.cs` (new) — `CeldaPlanilla` (Vacia, Numero(decimal), Texto(string),
  Otro), `FilaPlanilla(int Numero, CeldaPlanilla Codigo, CeldaPlanilla Precio)`, `ContenidoPlanilla(CeldaPlanilla
  EncabezadoA, CeldaPlanilla EncabezadoB, bool TieneColumnasDeMas, IReadOnlyList<FilaPlanilla> Filas)`,
  `RazonNoAplicada` (CodigoInexistente, PrecioInvalido, CodigoRepetido) con sus textos, `ResultadoFilaPlanilla`, y la
  clase estática `PlanillaCostos` con `TieneFormatoEsperado` y `Evaluar`.
- `src/Optica.Domain/Catalogo/ResultadoCostoNuevo.cs` (new) — Aplicado(Articulo, bool Cambia) o FueraDeRango.
- `src/Optica.Domain/Catalogo/Articulo.cs` (modified) — `ConCostoNuevo(decimal costo, ConfiguracionNegocio vigente)`;
  doc de `Activo` y del método con la excepción de D-12.
- `src/Optica.Domain/Catalogo/PoliticaArticulo.cs` (modified) — `EvaluarCostoNuevo(decimal)` público que reutiliza la
  regla privada de rango (sin duplicarla) y `EsCodigoAdmisible(string)` (UTF-16 válido y hasta 50 caracteres).
- `tests/Optica.Domain.Tests/Catalogo/PlanillaCostosTests.cs` (new), `ArticuloTests.cs` (modified).

**Logic**
- `TieneFormatoEsperado(contenido)` (FR-03, FR-04): falso si `TieneColumnasDeMas`, o si A o B de la fila 1 no son texto
  igual, con `PlegadoBusqueda.Plegar`, a "codigo en el proveedor" y "precio de costo nuevo" en ese orden.
- `Evaluar(filas, articulosPorClave, configuracion)` devuelve un `ResultadoFilaPlanilla` por fila:
  1. Código: `Texto` o `Numero` (el lector ya lo trae como texto) recortado; vacío, `!EsCodigoAdmisible` → inexistente
     (D-10). Clave con `NormalizacionCodigo.Clave` (FR-07).
  2. Repetidos (FR-10): las claves con más de una fila → todas sus filas "código repetido en la planilla" (D-11).
  3. Clave sin artículo → "código inexistente".
  4. Precio (FR-08, D-03, D-04, D-09): solo `Numero`; se redondea a 2 con `AwayFromZero`; `EvaluarCostoNuevo` con
     infracción → inválido; `ConCostoNuevo` fuera de rango → inválido.
  5. Aplicada (FR-06, FR-09, FR-11, FR-12): con el artículo nuevo, `Cambia` (D-13), marca `PrecioNoPositivo` (costo ≤ 0,
     FR-14) y `Inactivo` (FR-15).
- `ConCostoNuevo` (FR-06, FR-11): con `CalculadoraPrecioVenta.Calcular(costo, margen, vigente)`; copia con costo y venta
  nuevos, mismo código, descripción, margen, estado y versión; si la venta queda fuera de rango, `FueraDeRango`.

**Data model**
- Reuses `Articulo` from FEAT-002a — no schema change; constraints as declared there: `CodigoProveedorClave` NOT NULL y
  única (ADR-009), `PrecioCosto` y `PrecioVenta` decimal NOT NULL, `Activo` y `Version` NOT NULL como tokens de
  concurrencia.

**Input validation**
- Encabezados: texto, comparados plegados, exactamente dos columnas.
- Código: texto recortado, UTF-16 válido, 1 a 50 caracteres.
- Precio: celda numérica, decimal dentro de `ReglasImporte.ImporteMaximo` después de redondear a 2.

**Error handling**
- Encabezados distintos o columnas de más — `TieneFormatoEsperado` falso; el caso de uso rechaza sin evaluar filas.
- Código vacío, inválido o demasiado largo — razón "código inexistente", sin excepción.
- Precio vacío, texto u otro tipo — razón "precio inválido".
- Precio fuera de rango o venta recalculada fuera de rango — razón "precio inválido".
- Código repetido — todas sus filas con "código repetido en la planilla"; el artículo queda sin cambios.

**Required tests**
- [ ] `ConCostoNuevo_AlicuotaYMultiplo_RecalculaVenta` — validates AC-01 ($2.420 → venta $3.630,00).
- [ ] `TieneFormatoEsperado_EncabezadosConMayusculasAcentosYEspacios_Acepta` — validates AC-06.
- [ ] `TieneFormatoEsperado_EncabezadosCodigoYPrecio_Rechazado` — encabezados inválidos (AC-05).
- [ ] `TieneFormatoEsperado_TerceraColumna_Rechazado` — columnas de más rechazadas (AC-04).
- [ ] `Evaluar_CodigoInexistente_ListaRazon` — validates AC-02.
- [ ] `Evaluar_CodigoVacioInvalidoOLargo_CodigoInexistente` — código inválido, sin excepción.
- [ ] `Evaluar_PrecioVacioYTexto_PrecioInvalido` — validates AC-03: precio vacío o texto inválido ("abc" y "2420" como texto, D-04).
- [ ] `Evaluar_PrecioFueraDeRangoOVentaFueraDeRango_PrecioInvalido` — precio inválido por rango.
- [ ] `Evaluar_PrecioConTresDecimales_RedondeaAlCentavo` — D-03 (1234,565 → 1234,57; -0,005 → -0,01).
- [ ] `Evaluar_PrecioNegativoYCero_AplicaYMarca` — validates AC-08.
- [ ] `Evaluar_CodigoEnMinusculasYConEspacios_Aplica` — validates AC-09 y AC-10.
- [ ] `Evaluar_CodigoDuplicadoConOtraCaja_AmbasRechazadas` — validates AC-11: código duplicado, ambas filas rechazadas.
- [ ] `Evaluar_ArticuloInactivo_AplicaYMarcaInactivo` — validates AC-12 (sigue inactivo, margen igual).
- [ ] `Evaluar_CostoYVentaIguales_AplicadaSinCambio` — D-13.

**Completion criterion**
Las pruebas pasan, el dominio sigue sin dependencias nuevas y `ConCostoNuevo` nunca lanza por desborde.

## Block 2 — Lector del .xlsx

**Files**
- `src/Optica.Application/Catalogo/ILectorPlanillaCostos.cs` (new) — puerto: `LecturaPlanilla Leer(ReadOnlyMemory<byte>
  archivo)`, con `LecturaPlanilla` = Leida(ContenidoPlanilla) | NoEsXlsx | ExcedeLimites.
- `src/Optica.Infrastructure/Planillas/LectorPlanillaCostosXlsx.cs` (new) — implementación con OpenXml.
- `src/Optica.Infrastructure/Optica.Infrastructure.csproj` (modified) — `DocumentFormat.OpenXml` `[3.1.1]`.
- `src/Optica.Infrastructure/packages.lock.json`, `src/Optica.Api/packages.lock.json`,
  `tests/Optica.Infrastructure.Tests/packages.lock.json`, `tests/Optica.Api.Tests/packages.lock.json` (modified).
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — singleton `ILectorPlanillaCostos`.
- `tests/Optica.Infrastructure.Tests/Planillas/LectorPlanillaCostosXlsxTests.cs` (new) — genera los .xlsx en la prueba
  con el SDK.
- `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs` (modified) — `Api_NoReferenciaOpenXml`.

**Logic**
- Abre `SpreadsheetDocument.Open(new MemoryStream(...), false, new OpenSettings { MaxCharactersInPart = 100_000_000 })`.
  Firma no ZIP, paquete sin libro, sin hojas, o cualquier `OpenXmlPackageException`, `InvalidDataException`,
  `XmlException`, `IOException` o `InvalidOperationException` del SDK → `NoEsXlsx` sin mensaje ni causa (D-17, R-05).
- Primera hoja según `workbook.xml` (`Sheets.First()` → relación) (D-05). Cadenas compartidas leídas con
  `OpenXmlReader` sobre `SharedStringTablePart` (texto de `t` concatenado de los `r`).
- Hoja con `OpenXmlReader`: por cada `Cell`, columna por la referencia `r`; celda sin `CellValue` ni `InlineString` →
  vacía (D-08). Tipo `s` → texto de la tabla; `inlineStr`/`str` → texto; `n` o sin tipo → `Numero` con
  `decimal.TryParse(texto, NumberStyles.Float, InvariantCulture)` (fallo → `Otro`); `b`, `e`, `d` → `Otro`. Para el
  código, un `Numero` se entrega como texto con `ToString(CultureInfo.InvariantCulture)`.
- Columna C o siguiente con celda no vacía en cualquier fila → `TieneColumnasDeMas`.
- Filas con A y B vacías se descartan (D-08). Límites (D-14): más de 50.000 filas con datos, índice de fila mayor que
  50.001 o celda de más de 65.536 caracteres → `ExcedeLimites`.
- Sin DTD: el SDK no procesa entidades; el lector no usa `XmlDocument` ni `XmlReader` propios.

**Data model**
- Reuses `Articulo` from FEAT-002a — no schema change; the lector no toca la base ni el disco; constraints as declared
  there: `CodigoProveedorClave` NOT NULL y única, `PrecioCosto` y `PrecioVenta` decimal NOT NULL, `Activo` y `Version`
  NOT NULL como tokens de concurrencia.

**Input validation**
- Bytes del archivo hasta 10 MB (el controlador ya acotó el cuerpo); paquete OpenXml con libro y al menos una hoja.
- Hasta 50.000 filas con datos, índice de fila ≤ 50.001, celdas de hasta 65.536 caracteres, partes de hasta
  100.000.000 de caracteres.

**Error handling**
- Archivo .xls (firma OLE), .csv, PDF o ZIP sin libro — `NoEsXlsx`.
- XML mal formado o con DTD y entidad externa — `NoEsXlsx`, sin leer el recurso externo.
- Bomba ZIP o parte demasiado grande — `NoEsXlsx` por el tope de caracteres, sin agotar memoria.
- Más filas, índice de fila o celda más larga que los límites — `ExcedeLimites`.

**Required tests**
- [ ] `Leer_PlanillaValida_DevuelveEncabezadosYFilasTipadas` — validates AC-01 (lectura).
- [ ] `Leer_ArchivoXlsYCsv_NoEsXlsxRechazado` — validates AC-07: .xls y .csv rechazados.
- [ ] `Leer_XmlConEntidadExterna_RechazadoSinLeerElArchivo` — XML con entidad externa rechazado (R-03).
- [ ] `Leer_BombaZip_RechazadoSinAgotarMemoria` — bomba rechazada (R-02).
- [ ] `Leer_MasDe50000FilasOIndiceAlto_ExcedeLimitesRechazado` — límites excedidos rechazados (R-02).
- [ ] `Leer_TerceraColumnaConDato_MarcaColumnasDeMas` — validates AC-04 (lectura).
- [ ] `Leer_CeldaConFormatoSinValorEnC_NoMarcaColumnasDeMas` — D-08.
- [ ] `Leer_PrecioTextoNumeroYCientifico_TiposCorrectos` — D-09.
- [ ] `Leer_CodigoNumerico_TextoInvariante` — D-10.
- [ ] `Leer_PrimeraHojaPorOrdenDelLibro` — D-05.
- [ ] `Leer_ExcepcionDelPaquete_NoEsXlsxSinMensaje` — error sin datos (R-05).
- [ ] `Api_NoReferenciaOpenXml` — ADR-031.

**Completion criterion**
Las pruebas pasan; `dotnet restore` en modo bloqueado funciona con los lock files nuevos.

## Block 3 — Caso de uso y persistencia

**Files**
- `src/Optica.Domain/Catalogo/IArticuloRepository.cs` (modified) — `RegistrarCostoActualizado(Articulo articulo)`.
- `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs` (modified) — implementación: `AutoDetectChanges`
  apagado y restaurado en `finally`, entrada seguida o `Attach` con activo y versión leídos como tokens, solo
  `PrecioCosto`, `PrecioVenta` y `Version` (`checked` +1) modificados, guarda propia `SoloCostoPrecioYVersionModificados`.
- `src/Optica.Application/Catalogo/ActualizarCostosPorPlanilla.cs` (new) — caso de uso y `ResultadoPlanillaCostos`
  (Aplicada con filas leídas, aplicadas, no aplicadas y para revisar | NoEsXlsx | FormatoInesperado | ExcedeLimites).
- `src/Optica.Api/Program.cs` (modified) — `AddScoped<ActualizarCostosPorPlanilla>()`.
- `src/Optica.Api/appsettings.json` (modified) — `Logging:EventLog:LogLevel:Optica.Application.Catalogo.Planilla`.
- `tests/Compartido/EncuentroDeAltas.cs`, `tests/Optica.Api.Tests/Catalogo/ArticulosApiTests.cs`,
  `tests/Optica.Application.Tests/Catalogo/Dobles.cs`, `tests/Optica.Application.Tests/Catalogo/ArticulosEnMemoriaTests.cs`
  (modified) — dobles de `IArticuloRepository`.
- `tests/Optica.Application.Tests/Catalogo/ActualizarCostosPorPlanillaTests.cs` (new).
- `tests/Optica.Infrastructure.Tests/Persistencia/ArticuloRepositoryTests.cs` (modified) — batería espejo de
  `RegistrarPrecioRecalculado`.
- `tests/Optica.Infrastructure.Tests/Catalogo/ActualizarCostosRendimientoTests.cs` (new) — AC-13.
- `tests/Optica.Api.Tests/Configuracion/ConfiguracionApiTests.cs` (modified) — categoría en EventLog.

**Logic**
1. `lector.Leer(archivo)` fuera de la transacción. `NoEsXlsx` / `ExcedeLimites` → devuelve el rechazo sin tocar la base.
2. `PlanillaCostos.TieneFormatoEsperado` falso → `FormatoInesperado` (FR-04).
3. En `IUnidadDeTrabajo.EjecutarEnTransaccionAsync`: configuración vigente, `ListarParaRecalculoAsync`, diccionario por
   `CodigoProveedorClave`, `PlanillaCostos.Evaluar`, `RegistrarCostoActualizado` solo para las aplicadas que cambian
   (D-13), una `ConfirmarAsync` (D-15, R-06).
4. Después del commit, log Information `Optica.Application.Catalogo.Planilla` (D-16): filas leídas, aplicadas, no
   aplicadas por razón, precio no positivo, inactivas y fecha UTC.

**Data model**
- Reuses `Articulo` from FEAT-002a — no schema change; constraints as declared there: `CodigoProveedorClave` NOT NULL y
  única, `PrecioCosto` y `PrecioVenta` decimal NOT NULL, `Activo` y `Version` NOT NULL como tokens de concurrencia
  (`IsConcurrencyToken`), sin triggers sobre la tabla.

**Input validation**
- Archivo: bytes no vacíos (el controlador lo exige); el resto lo validan el lector (Block 2) y el dominio (Block 1).

**Error handling**
- Planilla no .xlsx o que excede límites — rechazo sin abrir la transacción; nada se actualiza.
- Formato inesperado — rechazo sin abrir la transacción; nada se actualiza (FR-04).
- Conflicto de concurrencia al confirmar — la transacción se revierte, se propaga `ConflictoAlConfirmarException` y el
  controlador responde 409; nada queda aplicado.
- Falla al confirmar o interrupción — la transacción se revierte, la excepción se propaga y no se registra la carga.

**Required tests**
- [ ] `Ejecutar_FilasValidasEInexistente_AplicaYListaRazon` — validates AC-02 (ArticulosEnMemoria).
- [ ] `Ejecutar_NoEsXlsx_RechazadoSinTocarLaBase` — no es .xlsx rechazado.
- [ ] `Ejecutar_FormatoInesperado_RechazadoSinTocarLaBase` — validates AC-04 y AC-05 (rechazado).
- [ ] `Ejecutar_ExcedeLimites_RechazadoSinTocarLaBase` — límites rechazados.
- [ ] `Ejecutar_ConflictoAlConfirmar_NadaAplicado` — conflicto de concurrencia.
- [ ] `Ejecutar_ErrorAlConfirmar_NoRegistraLaCarga` — error al confirmar, todo o nada (R-06).
- [ ] `Ejecutar_Log_SoloCantidadesSinCodigosNiPrecios` — R-05.
- [ ] `RegistrarCostoActualizado_ArticuloInactivo_SigueInactivo` — validates AC-12 (SQLite real).
- [ ] `RegistrarCostoActualizado_VersionVieja_ConflictoAlConfirmar` — versión vieja da conflicto.
- [ ] `RegistrarCostoActualizado_EntradaConOtrosCambios_Rechazado` — guarda propia.
- [ ] `Planilla10000Filas_Catalogo10000_MenosDe120SegundosY10EnTransaccion` — validates AC-13.
- [ ] `EventLog_IncluyeCategoriaPlanilla` — D-16.

**Completion criterion**
Las pruebas pasan, AC-13 mide menos de 120 s total y menos de 10 s dentro de la transacción, y los dobles compilan.

## Block 4 — Endpoint de la Api

**Files**
- `src/Optica.Api/Controllers/PlanillaCostosController.cs` (new) — `POST api/articulos/planilla-costos`.
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified, si hace falta) — 400 con `errors.planilla`.
- `tests/Optica.Api.Tests/Catalogo/PlanillaCostosApiTests.cs` (new).
- `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs`, `tests/Optica.Api.Tests/Acceso/SinCacheTests.cs` (modified).

**Logic**
Deserializa `SolicitudPlanillaCostos(string? Planilla)` con `PrintMembers => false`, decodifica base64 con
`Convert.TryFromBase64String` sobre un búfer de 10 MB, llama al caso de uso y traduce el resultado. Solo traduce: no
decide reglas (AGENTS.md).

**API contract**
- Method + path: `POST /api/articulos/planilla-costos`
- Request: `application/json` `{ "planilla": string }` (base64 del .xlsx, hasta 10 MB decodificado).
- Response: 200 `{ "filasLeidas": int, "aplicadas": int, "noAplicadas": [{ "fila": int, "codigo": string,
  "razon": "código inexistente" | "precio inválido" | "código repetido en la planilla" }], "revisar": [{ "fila": int,
  "codigo": string, "precioCosto": number, "indicaciones": ["actualizado con precio negativo o cero" | "inactivo"] }] }`;
  `codigo` hasta 60 caracteres (D-10); `Cache-Control: no-store`.
- Error codes: 400 ProblemDetails con `errors.planilla` (falta, base64 inválido, no es .xlsx, formato inesperado,
  excede límites); 401 sin sesión; 409 `ConflictoArticulo` motivo "cambiado"; 413 cuerpo de más de 14 MB; 415 no JSON.
- Auth: sesión de la operadora (cookie `HttpOnly`, `Secure`, `SameSite=Strict`); política por defecto de la Api.

**Data model**
- Reuses `Articulo` from FEAT-002a — no schema change; constraints as declared there: clave única NOT NULL, costo y
  venta NOT NULL, `Activo` y `Version` como tokens de concurrencia.

**Input validation**
- `planilla`: string obligatorio, base64 válido, decodificado entre 1 byte y 10 MB; cuerpo hasta 14 MB
  (`[RequestSizeLimit]`); JSON con `JsonNumberHandling.Strict`.

**Error handling**
- `planilla` faltante o vacía — 400 "Elegí una planilla .xlsx.".
- base64 inválido o archivo no .xlsx — 400 "El archivo no es una planilla .xlsx. Guardala como Libro de Excel (.xlsx)
  y volvé a cargarla." (FR-05, AC-07).
- Formato inesperado — 400 "El formato de la planilla no es el esperado. Tiene que tener solo dos columnas, con los
  encabezados \"Codigo en el proveedor\" y \"Precio de Costo Nuevo\" en la fila 1." (FR-05, AC-04, AC-05).
- Excede límites — 400 "La planilla supera las 50.000 filas o tiene celdas demasiado largas. Dividila y cargala por
  partes.".
- Conflicto de concurrencia — 409 motivo "cambiado".
- Cuerpo de más de 14 MB — 413; no JSON — 415; sin sesión — 401.

**Required tests**
- [ ] `Post_PlanillaValida_200ConResultado` — validates AC-01, AC-02, AC-08, AC-12 por la Api.
- [ ] `Post_PlanillaFaltante_400` — planilla faltante.
- [ ] `Post_Base64Invalido_400NoEsXlsx` — base64 inválido.
- [ ] `Post_ArchivoCsv_400PideXlsx` — validates AC-07 (inválido).
- [ ] `Post_TerceraColumna_400FormatoNoEsperadoYNadaActualizado` — validates AC-04 (rechazado).
- [ ] `Post_ExcedeLimites_400Rechazado` — límites rechazados.
- [ ] `Post_Conflicto_409Cambiado` — conflicto.
- [ ] `Post_CuerpoDeMas_413` y `Post_Multipart_415` — rechazados (R-01, R-02).
- [ ] `Post_SinSesion_401` — no autorizado.
- [ ] `PlanillaCostos_NoStore` — no-store.

**Completion criterion**
Las pruebas pasan y el endpoint responde según el contrato.

## Block 5 — Pantalla "Actualizar costos"

**Files**
- `web/src/paginas/ActualizarCostos.tsx` (new) y `ActualizarCostos.test.tsx` (new).
- `web/src/App.tsx` (modified) — ruta `/articulos/actualizar-costos`; `web/src/App.test.tsx` (modified) — PANTALLAS.
- `web/src/paginas/Articulos.tsx` (modified) — enlace "Actualizar costos con planilla" junto a "Nuevo artículo";
  `Articulos.test.tsx` (modified).
- `web/src/api/cliente.ts` (modified) — `actualizarCostosPorPlanilla(archivo: File)`: lee el archivo, lo pasa a base64
  y hace el POST JSON con `hacerPedido`; 413 → "La planilla supera los 10 MB."; `cliente.test.ts` (modified).

**Logic**
Elegir archivo (`accept=".xlsx"`), botón "Actualizar costos" (deshabilitado sin archivo y mientras procesa, con texto
"Procesando…"), y al terminar: "Se actualizaron N artículos de M filas.", tabla "Filas no aplicadas" (fila, código,
razón) y tabla "Para revisar" (fila, código, precio de costo, indicaciones). El error de la Api se muestra en el aviso
de error con el texto de `errors.planilla`. Todo por JSX (R-08). Ayuda: "La columna A conviene con formato Texto para
no perder ceros a la izquierda."

**Data model**
- Reuses `Articulo` from FEAT-002a — no schema change; la pantalla solo consume el contrato del Block 4; constraints as
  declared there: `CodigoProveedorClave` NOT NULL y única, `PrecioCosto` y `PrecioVenta` decimal NOT NULL, `Activo` y
  `Version` NOT NULL como tokens de concurrencia.

**Input validation**
- Archivo obligatorio, uno solo; tamaño ≤ 10 MB verificado antes de enviar (mismo texto que el 413).

**Error handling**
- Sin archivo elegido — botón deshabilitado; no se envía nada.
- Archivo de más de 10 MB — mensaje "La planilla supera los 10 MB." sin enviar.
- Rechazo 400 de la Api (no .xlsx, formato, límites) — aviso de error con el texto recibido.
- Conflicto 409 o error de red — aviso de error con el texto común del cliente; la operadora puede reintentar.

**Required tests**
- [ ] `muestra el resultado con aplicadas, no aplicadas y para revisar` — validates AC-02, AC-08, AC-12 en pantalla.
- [ ] `sin archivo el botón queda deshabilitado (faltante)` — archivo faltante.
- [ ] `archivo de más de 10 MB rechazado sin enviar` — rechazado.
- [ ] `muestra el error de formato inválido de la Api` — validates AC-04, AC-05, AC-07 en pantalla.
- [ ] `muestra el error de conflicto 409` — conflicto.
- [ ] `un código con HTML se muestra como texto` — R-08.
- [ ] `Articulos tiene el enlace a Actualizar costos` y `App` con la ruta en PANTALLAS.

**Completion criterion**
Vitest, lint y build pasan; la cobertura web no baja del 80 %.

## Block 6 — e2e en Chrome y Edge

**Files**
- `e2e/tests/actualizar-costos.spec.ts` (new) — corre en Chrome y Edge estables.
- `e2e/tests/xlsx-minimo.ts` (new) — arma un .xlsx (ZIP con `[Content_Types].xml`, relaciones, `workbook.xml`, una hoja
  con cadenas en línea y números) con `node:zlib` (`deflateRawSync`, `crc32`), sin paquetes npm (D-18).
- `e2e/tests/xlsx-minimo.test.mjs` o prueba de `npm run test:unidad` (new) — el ZIP generado tiene CRC y tamaños
  correctos.
- `e2e/README.md` (modified) — tabla de proyectos y puertos.

**Logic**
Verifica FR-01, FR-02, FR-04, FR-05, FR-06, FR-09, FR-10, FR-11, FR-13, FR-14 y FR-15 de punta a punta. Siembra
artículos por la Api (molde `configuracion-recalculo.spec.ts`), entra a "Actualizar costos", carga planillas
generadas con `setInputFiles` (buffer) y verifica la pantalla y los artículos por la Api.

**Data model**
- Reuses `Articulo` from FEAT-002a — no schema change; los artículos se siembran por la Api; constraints as declared
  there: `CodigoProveedorClave` NOT NULL y única, `PrecioCosto` y `PrecioVenta` decimal NOT NULL, `Activo` y `Version`
  NOT NULL como tokens de concurrencia.

**Input validation**
- Planillas generadas por la prueba: válidas, con fila inexistente, con tercera columna, un .csv, precios -100 y 0,
  códigos repetidos e inactivo.

**Error handling**
- Planilla con tercera columna — la prueba verifica el mensaje de formato y que ningún artículo cambió.
- Archivo .csv — la prueba verifica el mensaje que pide .xlsx.

**Required tests**
- [ ] `actualiza costo y venta y lista la fila inexistente` — validates AC-01 y AC-02 (NFR-02).
- [ ] `planilla con tercera columna rechazada sin actualizar` — validates AC-04 (NFR-02).
- [ ] `archivo csv rechazado pide xlsx` — validates AC-07 (NFR-02).
- [ ] `lista precio negativo o cero, repetidos e inactivo` — validates AC-08, AC-11 y AC-12 (NFR-02).
- [ ] `xlsx-minimo arma un zip válido` — prueba de unidad del generador; un CRC inválido sería un error.

**Completion criterion**
`npm run test:unidad` y `npm run test:harness` pasan en Chrome y Edge, con el proyecto nuevo en los dos navegadores.

## Final verification
- Sin cambios de esquema ni migraciones: el rollback es revertir los commits del ticket; los costos ya aplicados por
  una planilla se corrigen cargando otra.
- `dotnet build` sin advertencias, `dotnet test` en verde, `dotnet format --verify-no-changes`, web lint/test/build y
  e2e en Chrome y Edge.
- Los 13 AC con prueba; AC-13 medido por debajo de 120 s.
- `validate_spec.py --tier FEATURE` y `validate_threat.py` pasan sobre los documentos finales.
