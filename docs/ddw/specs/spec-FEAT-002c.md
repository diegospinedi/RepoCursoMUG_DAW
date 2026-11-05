# Spec FEAT-002c: Recálculo del catálogo por configuración y volumen

| Field | Value |
|-------|-------|
| Ticket | FEAT-002c |
| PRD | docs/ddw/prd/prd-FEAT-002c.md |
| Tier | FEATURE |
| Date | 2026-11-05 |
| Spec loops | 3 |
| Loops since last human decision | 0 |

## Summary
Grabar un cambio de alícuota, condición fiscal o múltiplo recalcula el precio de venta de todo el catálogo, activos e
inactivos, en la misma transacción que la configuración (FR-01, FR-02). El dominio decide qué parámetros disparan el
recálculo y calcula cada precio (ADR-010); un caso de uso de Catálogo (D-10) lee la configuración y los artículos
dentro de una transacción de escritura que abarca lecturas y grabación (ADR-013), rechaza el cambio con los códigos
de los artículos cuyo precio quedaría fuera de rango (D-01, D-08) y graba solo los precios que cambian, incrementando
su versión (D-04). La pantalla de configuración informa cuántos precios cambiaron (D-06, D-09). Una prueba de volumen
con 10.000 artículos verifica los resultados de FEAT-002a y FEAT-002b y el tiempo de la búsqueda (D-02, D-13).
Decisiones: `docs/ddw/specs/decisions-FEAT-002c.md` (D-01 a D-24).
Decisión del usuario del 2026-11-05 al llegar al techo de rondas: la prueba de tiempo corre en la suite normal con
el riesgo de carga aceptado y además en un paso propio del cierre de CODE (D-21).

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2, Block 3, Block 4, Block 6 |
| FR-02 | Block 2, Block 3, Block 4, Block 6 |
| NFR-01 | Strategy: Block 5 siembra 10.000 artículos (D-15) y verifica alta, modificación, búsqueda, desactivación y recálculo con los mismos resultados (AC-05), y el p95 de la búsqueda menor a 1 s (D-02, D-13) |
| NFR-02 | Strategy: Block 6 corre AC-01 a AC-04 en los proyectos `chrome-configuracion-recalculo` y `msedge-configuracion-recalculo` (canal estable instalado); la versión anterior de cada navegador se verifica a mano, como en FEAT-001 y FEAT-002 |

## Dependencies between blocks
Block 1 → Block 2 → Block 3 → Block 4 → Block 5 → Block 6. Cada bloque usa los tipos del anterior.

## Block 1 — Dominio: parámetros que cambian el precio y recálculo de un artículo

**Files**
- `src/Optica.Domain/Configuracion/ConfiguracionNegocio.cs` (modified) — `CambiaParametrosDePrecioRespectoDe(ConfiguracionNegocio anterior)`.
- `src/Optica.Domain/Catalogo/Articulo.cs` (modified) — `ConPrecioRecalculado(ConfiguracionNegocio vigente)`.
- `src/Optica.Domain/Catalogo/ResultadoRecalculo.cs` (new) — `SinCambio`, `Cambiado(articulo)` o `FueraDeRango`.
- `tests/Optica.Domain.Tests/Configuracion/ConfiguracionNegocioTests.cs` (modified)
- `tests/Optica.Domain.Tests/Catalogo/ArticuloTests.cs` (modified)

**Logic**
- `CambiaParametrosDePrecioRespectoDe(anterior)`: verdadero si cambia la alícuota, la condición fiscal o el múltiplo;
  falso si solo cambia el tope o no cambia nada (D-07). `anterior` nulo → `ArgumentNullException`.
- `ConPrecioRecalculado(vigente)`: calcula con `CalculadoraPrecioVenta` sobre el costo y el margen guardados, para
  activos e inactivos (AC-03). Precio igual al guardado → `SinCambio`; distinto → `Cambiado` con una copia con el
  mismo código, datos, estado y versión y el precio nuevo; infracción de rango → `FueraDeRango`.

**Data model**
- Entidad `Articulo` del dominio sin columnas nuevas: `PrecioVenta` (importe NOT NULL, centésimos por ADR-008) cambia
  en la copia; `Activo` y `Version` (NOT NULL) se conservan. Entidad `ConfiguracionNegocio` sin cambios: alícuota,
  condición y múltiplo NOT NULL, tope nullable.

**Input validation**
- `vigente` y `anterior`: no nulos; las configuraciones ya cumplen `PoliticaConfiguracionNegocio`.

**Error handling**
- Precio fuera de rango con la configuración nueva — `FueraDeRango`, nunca desborde.
- Configuración nula — `ArgumentNullException` sin valores.

**Required tests**
- [ ] `ConPrecioRecalculado_PrecioFueraDeRango_Rechazado` — precio inválido rechazado como `FueraDeRango`
- [ ] `CambiaParametrosDePrecio_AnteriorNulo_LanzaError` — configuración nula rechazada
- [ ] `ConPrecioRecalculado_Multiplo50_1815A1850` — validates AC-02
- [ ] `ConPrecioRecalculado_Inactivo_SeRecalcula` — validates AC-03
- [ ] `ConPrecioRecalculado_AlicuotaDistinta_SinCambio` — ADR-010 (validates AC-01: 1.105 y 50 % dan 1.657,50 con alícuota 10,5)
- [ ] `CambiaParametrosDePrecio_SoloTope_Falso` — D-07
- [ ] `CambiaParametrosDePrecio_AlicuotaCondicionOMultiplo_Verdadero` — D-07

**Completion criterion**
Las 7 pruebas pasan y `Optica.Domain.Tests` queda en verde.

## Block 2 — Infraestructura: transacción que abarca lecturas y escritura de precios recalculados

**Files**
- `src/Optica.Application/Comun/IUnidadDeTrabajo.cs` (modified) — `EjecutarEnTransaccionAsync<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken)` y documentación de `ConfirmarAsync` dentro de la operación (ADR-013).
- `src/Optica.Infrastructure/Persistencia/UnidadDeTrabajoEf.cs` (modified) — implementación con `BeginTransactionAsync` (`BEGIN IMMEDIATE`); `ConfirmarAsync` reutiliza `Database.CurrentTransaction` y solo graba.
- `src/Optica.Domain/Catalogo/IArticuloRepository.cs` (modified) — `ListarParaRecalculoAsync` y `RegistrarPrecioRecalculado(Articulo)`.
- `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs` (modified)
- `tests/Optica.Infrastructure.Tests/Persistencia/UnidadDeTrabajoTests.cs` (new o el archivo de pruebas de la unidad que exista) y `ArticuloRepositoryTests.cs` (modified)
- `docs/adr/adr-013-transaccion-de-escritura-que-abarca-lecturas.md` y la documentación XML de `IUnidadDeTrabajo`: la operación no se anida y nada dentro abre otra conexión que escriba (D-19).
- Dobles de `IUnidadDeTrabajo` que implementan el miembro nuevo ejecutando la operación: `tests/Optica.Application.Tests/Acceso/Dobles.cs`, `Acceso/DefinirContrasenaTests.cs`, `Catalogo/Dobles.cs`, `Configuracion/Dobles.cs`, `tests/Optica.Api.Tests/Acceso/ComandoRestablecerContrasenaTests.cs`, `tests/Optica.Api.Tests/Catalogo/ArticulosApiTests.cs` (`UnidadQueSeCruza`, `ArticulosQueFallan`; `UnidadQueSeCruza` lanza si recibe una operación con un cruce pendiente, D-19) (modified)

**Logic**
- `EjecutarEnTransaccionAsync`: abre la transacción, ejecuta la operación, confirma; si la operación lanza, revierte,
  limpia el contexto y propaga. Una operación anidada lanza `InvalidOperationException` (no se anida).
- `ConfirmarAsync` dentro de la operación: solo `SaveChanges` (con el savepoint de EF); los mismos motivos de
  conflicto de D-18 de FEAT-002b; fuera de una operación, sin cambios.
- `ListarParaRecalculoAsync`: todos los artículos, sin seguimiento, como entidades (`AEntidad`).
- `RegistrarPrecioRecalculado(articulo)`: adjunta la fila con `Version` y `Activo` leídos como valores originales y
  marca solo `PrecioVenta` y `Version` (`checked`, +1) (D-04).

**Data model**
- Reuses `Articulo` from FEAT-002b — no schema change; constraints as declared there: NOT NULL, CHECK de largos y
  margen, `Activo` IN (0,1), `Version` ≥ 0, índice UNIQUE de `CodigoProveedorClave`, tokens `Version` y `Activo`.
- Reuses `ConfiguracionNegocio` from FEAT-001c — no schema change; constraints as declared there: una sola fila
  (`Id` = 1), NOT NULL en alícuota, condición y múltiplo.
- Rollback: no hay migraciones en este ticket; revertir es revertir el código, sin cambios en la base.

**Input validation**
- `operacion` no nula; `RegistrarPrecioRecalculado` rechaza un artículo sin código.

**Error handling**
- Operación anidada — `InvalidOperationException`.
- Falla dentro de la operación — reversión completa y la excepción se propaga.
- Conflicto al grabar dentro de la operación — `ConflictoAlConfirmarException` con su motivo; la transacción externa revierte.
- Artículo sin código — `ArgumentException` sin valores.

**Required tests**
- [ ] `EjecutarEnTransaccion_Anidada_LanzaError` — operación anidada rechazada
- [ ] `EjecutarEnTransaccion_OperacionLanza_RevierteTodo` — error revierte la configuración y los precios
- [ ] `EjecutarEnTransaccion_ConflictoDeUnicidad_NoGrabaNada` — conflicto dentro de la operación rechazado sin escritura
- [ ] `RegistrarPrecioRecalculado_SinCodigo_Rechazado` — artículo inválido rechazado
- [ ] `EjecutarEnTransaccion_ConfirmaLecturasYGrabacionJuntas` — FR-02
- [ ] `RegistrarPrecioRecalculado_Inactivo_GrabaPrecioYVersion` — validates AC-03
- [ ] `RegistrarPrecioRecalculado_SoloCambiaPrecioYVersion` — D-04
- [ ] `ListarParaRecalculo_DevuelveActivosEInactivos` — validates AC-03
- [ ] `EjecutarEnTransaccion_ConflictoAtendido_ConfirmaSinCambios` — conflicto rechazado dentro de la operación: 0 filas nuevas y sin excepción (D-18)

**Completion criterion**
Las 9 pruebas pasan y `Optica.Infrastructure.Tests` y `Optica.Application.Tests` compilan y quedan en verde con los
dobles actualizados.

## Block 3 — Aplicación: grabación de la configuración con recálculo

**Files**
- `src/Optica.Application/Catalogo/GrabarConfiguracionConRecalculo.cs` (new) — caso de uso y `ResultadoGrabacionConfiguracion` (D-10).
- `src/Optica.Application/Configuracion/GrabarConfiguracionNegocio.cs` (removed) y `tests/Optica.Application.Tests/Configuracion/GrabarConfiguracionNegocioTests.cs` (removed; sus casos pasan al caso de uso nuevo, abajo).
- `src/Optica.Application/Configuracion/DatosConfiguracion.cs` (new) — el record, mismo espacio de nombres (D-16).
- `tests/Optica.Application.Tests/Arquitectura/DependenciasDeModulosTests.cs` (new) — Configuración no usa tipos de Catálogo (D-16).
- `src/Optica.Application/Catalogo/AltaArticulo.cs` y `ModificarArticulo.cs` (modified) — dentro de `EjecutarEnTransaccionAsync` (D-05).
- `src/Optica.Application/Catalogo/ValidacionArticulo.cs` (modified) — texto del error de D-01 y D-08.
- `tests/Optica.Application.Tests/Catalogo/Dobles.cs` (modified) — doble combinado de configuración y artículos con el comportamiento de D-20.
- `tests/Optica.Application.Tests/Catalogo/GrabarConfiguracionConRecalculoTests.cs` (new); `AltaArticuloTests.cs` y `ModificarArticuloTests.cs` (modified)

**Logic**
- Valida con `ValidacionConfiguracion` (sin leer nada si hay errores). Dentro de `EjecutarEnTransaccionAsync`: lee la
  vigente; si `CambiaParametrosDePrecioRespectoDe` es falso, solo reemplaza y confirma. Si es verdadero, lista los
  artículos y calcula `ConPrecioRecalculado` para cada uno; si alguno da `FueraDeRango`, devuelve `Invalido` sin
  registrar nada (D-03); si no, reemplaza la configuración, registra los `Cambiado` y confirma. Después del commit
  registra en el log, con la categoría `Optica.Application.Configuracion.Grabacion` (constante pública del caso de uso, D-17), los valores anteriores y nuevos, la fecha UTC y la cantidad de precios cambiados (D-11).
- `ResultadoGrabacionConfiguracion`: `Grabado(cantidadCambiados)` o `Invalido(errores)`.
- `AltaArticulo` y `ModificarArticulo`: el mismo comportamiento de FEAT-002a y FEAT-002b; dentro de la operación van
  las lecturas, las verificaciones, el cálculo, el registro, la confirmación y la captura del conflicto; la relectura
  final, después (D-18).

**Data model**
- Reuses `Articulo` and `ConfiguracionNegocio` from Block 2 — no schema change; constraints as declared there: NOT NULL,
  CHECK, índice UNIQUE de la clave, tokens `Version` y `Activo`, fila única de configuración.

**Input validation**
- Los cuatro datos de la configuración: las reglas de `ValidacionConfiguracion` de FEAT-001c, sin cambios.

**Error handling**
- Datos de configuración inválidos — `Invalido` con todos los errores, sin leer ni grabar.
- Precio fuera de rango para uno o más artículos — `Invalido` con el error en `multiploRedondeo`: "No se grabó el cambio: el precio de venta de N artículos sería demasiado grande (códigos …). Revisá esos artículos o el múltiplo." con hasta 10 códigos ordenados, la marca "inactivo" y el agregado de D-08 si corresponde (D-01, D-12).
- Falla al confirmar — se revierte todo y se propaga (500 genérico); no se registra la grabación.
- Sin fila de configuración — la excepción se propaga, como en FEAT-001c.

**Required tests**
- [ ] `Grabar_DatosInvalidos_NoLeeNiGraba` — datos inválidos rechazados
- [ ] `Grabar_PrecioFueraDeRango_RechazadoConCodigos` — validates AC-04: error en `multiploRedondeo`, nada grabado
- [ ] `Grabar_MasDe10FueraDeRango_RechazadoCon10Codigos` — D-01
- [ ] `Grabar_InactivoFueraDeRango_RechazadoConMarca` — D-08
- [ ] `Grabar_FallaAlConfirmar_NoRegistraNiGraba` — error al confirmar (en `ConfirmarAsync` y en el commit externo) revierte y no registra en el log (AC-04)
- [ ] `Grabar_SinFila_PropagaError` — error sin configuración
- [ ] `Grabar_Multiplo50_Recalcula1815A1850` — validates AC-02
- [ ] `Grabar_RecalculaInactivos` — validates AC-03
- [ ] `Grabar_SoloTope_NoRecalcula` — D-07
- [ ] `Grabar_Alicuota10coma5_CantidadCero` — validates AC-01: configuración grabada, 0 precios cambiados y ninguna `Version` cambia (D-09, D-22)
- [ ] `Grabar_RegistraEnElLogDespuesDeConfirmar` — D-11; verifica la categoría pedida a la fábrica (D-17)
- [ ] `Alta_DespuesDeCambiarAlicuota_1105y50_1657coma50` — validates AC-01
- [ ] `Alta_DespuesDeCambiarMultiplo50_1105y50_1700` — el alta lee la configuración nueva (D-22)
- [ ] `Grabar_Exitoso_RegistraValoresAnterioresYNuevos` — R-04 de FEAT-001c, migrada
- [ ] `Grabar_ConflictoAlConfirmar_SePropaga` — conflicto al confirmar rechazado, migrada
- [ ] `Grabar_VariosCamposInvalidos_DevuelveTodosLosErrores` — datos inválidos rechazados, migrada
- [ ] `Grabar_TopeAusente_GrabaSinTope` — migrada
- [ ] `Grabar_Monotributo_ObtenerDevuelveMonotributo` — migrada
- [ ] `Configuracion_NoUsaTiposDeCatalogo` — D-16

**Completion criterion**
Las 19 pruebas pasan, junto con las demás pruebas migradas de `GrabarConfiguracionNegocioTests`, y `Optica.Application.Tests` queda en verde, incluidas las pruebas del alta y la modificación.

## Block 4 — Api y web: respuesta con la cantidad y aviso en la pantalla de configuración

**Files**
- `src/Optica.Api/Controllers/ConfiguracionController.cs` (modified) — el POST usa `GrabarConfiguracionConRecalculo` y responde `ConfiguracionGrabada`, record de presentación (D-23).
- `src/Optica.Api/Program.cs` (modified) — registra el caso de uso nuevo y quita el anterior.
- `tests/Optica.Api.Tests/Configuracion/ConfiguracionApiTests.cs` (modified) — claves del POST; el GET no cambia; `Appsettings_GrabacionDeConfiguracion_VaAlRegistroDeEventos` con la categoría de D-17.
- `web/src/api/cliente.ts` y `cliente.test.ts` (modified) — tipo `ConfiguracionGrabada` con su propio lector para el POST; el GET sigue con `leerConfiguracion` (D-23).
- `web/src/paginas/Configuracion.tsx` y `Configuracion.test.tsx` (modified) — mensaje de D-06.

**Logic**
- POST `/api/configuracion`: 200 con la configuración vigente más `preciosCambiados`; 400 con los errores por campo,
  incluido el de D-01 en `multiploRedondeo`. El GET responde exactamente lo mismo que antes.
- La pantalla muestra "La configuración se grabó." seguido de "Cambió el precio de venta de N artículos." ("de 1
  artículo") o "Ningún precio de venta cambió."; el error de D-01 aparece junto al múltiplo como los demás errores.

**API contract**
- Method + path: `POST /api/configuracion`
- Request: `{ alicuotaIva: number, condicionFiscal: string, multiploRedondeo: number, topeIdentificacion: number? }` (sin cambios)
- Response 200: `{ alicuotaIva, condicionFiscal, multiploRedondeo, topeIdentificacion, faltaTope, preciosCambiados: int }`
- Error codes: 400 (datos inválidos o precio fuera de rango en `multiploRedondeo`), 401, 413, 415, 500 (falla al confirmar)
- Auth: sesión obligatoria, como en FEAT-001c.

**Data model**
- Reuses `ConfiguracionNegocio` and `Articulo` from Block 2 — no schema change; constraints as declared there: NOT NULL,
  CHECK, fila única de configuración, tokens del artículo.

**Input validation**
- La misma de FEAT-001c (números estrictos, tamaño y JSON obligatorio).
- La pantalla valida `preciosCambiados` como entero ≥ 0; si no, respuesta ilegible.

**Error handling**
- 400 con error en `multiploRedondeo` — se muestra junto al múltiplo.
- `preciosCambiados` ausente o inválido — mensaje general de respuesta ilegible.
- 500 — mensaje general de FEAT-001c; la configuración mostrada sigue siendo la anterior.

**Required tests**
- [ ] `Post_PrecioFueraDeRango_400EnMultiplo` — precio inválido rechazado con códigos
- [ ] `Configuracion_PreciosCambiadosInvalido_RespuestaIlegible` — respuesta inválida rechazada
- [ ] `Configuracion_Error500_MensajeGeneral` — error del servidor
- [ ] `Post_Multiplo50_200ConPreciosCambiados` — validates AC-02
- [ ] `Get_Configuracion_SinCambiosEnElContrato` — el GET no cambia
- [ ] `consultarConfiguracion_SinPreciosCambiados_SeLee` — el GET de la web no exige `preciosCambiados` (D-23)
- [ ] `Appsettings_GrabacionDeConfiguracion_VaAlRegistroDeEventos` — D-17
- [ ] `Configuracion_Grabada_MuestraCantidad` — D-06, D-09

**Completion criterion**
Las 8 pruebas pasan, `Optica.Api.Tests` y la suite web quedan en verde y `npm run build` limpio.

## Block 5 — Volumen: 10.000 artículos

**Files**
- `tests/Optica.Api.Tests/Catalogo/VolumenCatalogoTests.cs` (new) — prueba funcional de AC-05 y prueba de tiempo (Trait `Categoria=Volumen`, colección sin paralelismo).
- `tests/Optica.Api.Tests/Catalogo/SiembraCatalogo.cs` (new) — siembra con SQL parametrizado en una transacción (D-15).
- `tests/Optica.Infrastructure.Tests/Persistencia/ArticuloRepositoryTests.cs` (modified) — recálculo de 10.000 artículos con `BaseTemporal`.
- `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs` (modified solo si D-02 no se cumple, D-14).

**Logic**
Cubre NFR-01 con AC-05 y FR-01 con el recálculo sobre 10.000 artículos.
- Siembra 10.000 artículos válidos (importes en centésimos, clave con `NormalizacionCodigo.Clave`, algunos inactivos).
- AC-05: con esa base, alta, modificación, búsqueda (por código, código en el proveedor y descripción sin tildes),
  desactivación y recálculo dan los mismos resultados que en los AC de FEAT-002a, FEAT-002b y este documento.
- D-02 y D-13: 5 búsquedas de calentamiento y 20 por escenario; p95 < 1 s. Corre en la suite normal y en un paso
  propio del cierre de CODE (`--filter Categoria=Volumen`), que es la evidencia (D-21).

**Data model**
- Reuses `Articulo` from Block 2 — no schema change; constraints as declared there: NOT NULL, CHECK, índice UNIQUE de
  la clave, `Activo` y `Version`.

**Input validation**
- La siembra solo usa datos válidos; los textos de búsqueda los fija cada prueba.

**Error handling**
- Siembra con datos inválidos — la prueba falla en la siembra con un mensaje propio, antes de medir.

**Required tests**
- [ ] `Siembra_DatoInvalido_FallaAntesDeMedir` — siembra inválida rechazada
- [ ] `Volumen10000_AltaModificacionBusquedaYDesactivacion_MismosResultados` — validates AC-05
- [ ] `Volumen10000_Recalculo_Multiplo50` — validates AC-05 con AC-02; registra la duración y verifica menos de 10 s (D-21)
- [ ] `Volumen10000_Busqueda_P95MenorA1s` — D-02, D-13

**Completion criterion**
Las 4 pruebas pasan; si la de tiempo no cumple con la optimización de D-14, se detiene el ticket y se pregunta.

## Block 6 — E2E: AC-01 a AC-04 en Chrome y Edge

**Files**
- `e2e/tests/configuracion-recalculo.spec.ts` (new)
- `e2e/README.md` (modified) — proyectos y puertos.

**Logic**
Cada prueba fija al empezar, por la Api, la configuración de partida (D-24), crea sus artículos por la Api con sesión
sobre la base temporal de su proyecto y cambia la configuración desde la pantalla. AC-04 siembra un artículo de costo `ImporteMaximo` y margen 0 con el cuerpo JSON como texto, sube
el múltiplo a 50, verifica el error con el código junto al múltiplo y que ni la configuración ni los precios cambian
(por la Api, con `respuesta.text()`); es la última prueba del archivo.

**Data model**
- Reuses `Articulo` and `ConfiguracionNegocio` from Block 2 — no schema change; constraints as declared there: NOT NULL,
  CHECK de largos y margen, índice UNIQUE de `CodigoProveedorClave`, `Activo` IN (0,1), `Version` ≥ 0, fila única de
  configuración.

**Input validation**
- Las pruebas escriben datos válidos salvo el caso de AC-04.

**Error handling**
- Precio fuera de rango — la pantalla muestra el error rechazado junto al múltiplo.

**Required tests**
- [ ] `alicuota 10,5 y luego costo 1.105 con margen 50 graba 1.657,50` — validates AC-01 (y la configuración queda en 10,5, D-22)
- [ ] `multiplo 50 pasa 1.815,00 a 1.850,00 sin editar el articulo` — validates AC-02
- [ ] `el recalculo cambia tambien el precio de un articulo inactivo` — validates AC-03
- [ ] `multiplo 50 rechazado con el codigo del articulo y nada cambia` — validates AC-04

**Completion criterion**
Las 4 pruebas pasan en `chrome-configuracion-recalculo` y `msedge-configuracion-recalculo` con `npm run test:harness`.

## Final verification
Las suites completas pasan; AC-01 a AC-05 tienen prueba; `HasPendingModelChanges` es falso (sin migraciones); el
módulo Configuración no referencia a Catálogo (D-10 de FEAT-002a); el p95 de la búsqueda con 10.000 artículos es menor
a 1 s (D-02).
