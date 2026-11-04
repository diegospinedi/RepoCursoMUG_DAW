# Spec FEAT-002a: Alta de artículos con precio de venta calculado

| Field | Value |
|-------|-------|
| Ticket | FEAT-002a |
| PRD | docs/ddw/prd/prd-FEAT-002a.md |
| Tier | FEATURE |
| Date | 2026-11-04 |
| Spec loops | 1 |
| Loops since last human decision | 0 |

## Summary
Un módulo `Catalogo` nuevo en cada capa. El dominio calcula el precio de venta con la identidad exacta
costo × (100 + margen) / 100 y lo redondea hacia +∞ al múltiplo configurado (ADR-010, D-04, D-05); una política
de dominio valida los datos del artículo, una infracción por campo, como en FEAT-001c. La tabla `Articulo`
guarda importes y margen en centésimos (ADR-008) y una clave normalizada del código en el proveedor con índice
UNIQUE (ADR-009). El caso de uso de alta lee la configuración vigente en cada operación, calcula, verifica la
unicidad y graba; la Api expone `POST /api/articulos` y `GET /api/articulos/{codigo}` con sesión, y la SPA suma
la pantalla `/articulos/nuevo`. Decisiones: `docs/ddw/specs/decisions-FEAT-002a.md` (D-01 a D-12).

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 2, Block 3, Block 4, Block 5, Block 6 |
| FR-02 | Block 1, Block 2, Block 3, Block 4, Block 5 |
| FR-03 | Block 1, Block 3, Block 5 |
| FR-04 | Block 1, Block 3, Block 5 |
| FR-05 | Block 1, Block 3, Block 4, Block 5 |
| FR-06 | Block 1 |
| FR-07 | Block 1, Block 6 |
| FR-08 | Block 3 |
| FR-09 | Block 1, Block 6 |
| FR-10 | Block 2, Block 3 |
| FR-11 | Block 5 |
| NFR-01 | Strategy: Block 6 corre los 11 AC en los proyectos `chrome-articulos` y `msedge-articulos` (canal estable instalado); la versión anterior de cada navegador se verifica a mano, como en FEAT-001 |
| NFR-02 | Strategy: importes en centésimos enteros (ADR-008) y formato de 2 decimales en la pantalla (D-11); Block 1 rechaza más de 2 decimales |

## Dependencies between blocks
Block 1 → Block 2 → Block 3 → Block 4 → Block 5 → Block 6. Cada bloque usa los tipos del anterior.

## Block 1 — Dominio: reglas compartidas, política, calculadora y artículo

**Files**
- `src/Optica.Domain/Comun/ReglasImporte.cs` (new) — `ImporteMaximo` y `TieneMasDeDosDecimales(decimal)` (D-12).
- `src/Optica.Domain/Configuracion/PoliticaConfiguracionNegocio.cs` (modified) — usa `ReglasImporte`; mantiene `ImporteMaximo` como alias público para no romper a sus usuarios; sin cambio de comportamiento.
- `src/Optica.Domain/Catalogo/NormalizacionCodigo.cs` (new) — recorte y clave normalizada (ADR-009).
- `src/Optica.Domain/Catalogo/PoliticaArticulo.cs` (new) — reglas de FR-02 a FR-05 y D-02, D-03, D-06.
- `src/Optica.Domain/Catalogo/CalculadoraPrecioVenta.cs` (new) — FR-06, FR-07, FR-09 (ADR-010, D-04, D-05).
- `src/Optica.Domain/Catalogo/Articulo.cs` (new) — entidad.
- `src/Optica.Domain/Catalogo/IArticuloRepository.cs` (new) — contrato.
- `tests/Optica.Domain.Tests/Comun/ReglasImporteTests.cs` (new)
- `tests/Optica.Domain.Tests/Catalogo/PoliticaArticuloTests.cs` (new)
- `tests/Optica.Domain.Tests/Catalogo/CalculadoraPrecioVentaTests.cs` (new)
- `tests/Optica.Domain.Tests/Catalogo/ArticuloTests.cs` (new)

**Logic**
`NormalizacionCodigo.Recortar(texto)` quita todo espacio en blanco de Unicode del principio y del final
(`string.Trim()`: espacio común, espacio duro, tabulador y demás; D-13, decisión del usuario), en el código y en la
descripción; un texto hecho solo de espacios cuenta como vacío. `Clave(texto)` = `Recortar`, normalización NFC y `ToUpperInvariant`. El largo de D-02 se mide
en `string.Length` sobre el texto recortado.
`PoliticaArticulo.Evaluar(string? codigoProveedor, string? descripcion, decimal? precioCosto, decimal?
margenUtilidad)` devuelve una infracción por campo, nunca lanza, en este orden (la primera que se cumple gana):
- código: ausente o vacío tras recortar → `CodigoFaltante`; más de 50 → `CodigoDemasiadoLargo`.
- descripción: ausente o vacía tras recortar → `DescripcionFaltante`; más de 200 → `DescripcionDemasiadoLarga`.
- costo: ausente → `CostoFaltante`; más de 2 decimales → `CostoConMasDeDosDecimales`; |costo| > `ImporteMaximo`
  → `CostoFueraDeRango`. Cero y negativos son válidos (FR-05).
- margen: ausente → `MargenFaltante`; fuera de 0 a 1000 → `MargenFueraDeRango` (rango antes que escala: 1.000,01
  informa el rango, AC-05); más de 2 decimales → `MargenConMasDeDosDecimales`.
`CalculadoraPrecioVenta.Calcular(decimal costo, decimal margen, ConfiguracionNegocio config)` devuelve
`PrecioCalculado(decimal CostoSinIva, decimal PrecioSinIva, decimal PrecioVenta)`: `PrecioVenta` =
`RedondearHaciaArriba(costo × (100 + margen) / 100, config.MultiploRedondeo)` con `decimal.Ceiling(x / m) × m`
(D-04, D-05, ADR-010), igual en las dos condiciones fiscales (FR-06, FR-07); en Responsable Inscripto `CostoSinIva`
= costo / (1 + alícuota / 100) y `PrecioSinIva` = `CostoSinIva` × (1 + margen / 100), sin redondear (FR-06); en
Monotributo `CostoSinIva` = costo y `PrecioSinIva` = costo × (1 + margen / 100). Si el valor exacto o el redondeado
supera `ImporteMaximo` en módulo, `Calcular` devuelve la infracción `PrecioVentaFueraDeRango` en lugar del precio
(D-06).
`Articulo(long? codigo, string codigoProveedor, string descripcion, decimal precioCosto, decimal margenUtilidad,
decimal precioVenta)` evalúa la política y lanza `ArgumentException` sin el valor si hay infracciones; guarda el
código recortado, expone `CodigoProveedorClave`, la descripción recortada, los importes y `CostoNoPositivo`
(precio de costo ≤ 0, FR-05). `codigo` es null antes de grabar.
`IArticuloRepository`: `Task<bool> ExisteClaveAsync(string clave, CancellationToken)`, `void Agregar(Articulo)`
(solo registra el alta; la confirma `IUnidadDeTrabajo`), `Task<Articulo?> ObtenerAsync(long codigo,
CancellationToken)` y `Task<Articulo?> ObtenerPorClaveAsync(string clave, CancellationToken)` (para leer el código
asignado después de confirmar).

**Data model**
- Sin esquema propio (la tabla es de Block 2). Restricciones de la entidad: código `long?` (null antes de grabar,
  NOT NULL después); código en el proveedor `string` NOT NULL, 1 a 50; clave `string` NOT NULL, única; descripción
  `string` NOT NULL, 1 a 200; precio de costo `decimal` NOT NULL (2 decimales, |x| ≤ ImporteMaximo); margen
  `decimal` NOT NULL (0 a 1000, 2 decimales); precio de venta `decimal` NOT NULL (2 decimales, |x| ≤ ImporteMaximo).

**Input validation**
- código en el proveedor: texto requerido, 1 a 50 caracteres tras recortar espacios.
- descripción: texto requerido, 1 a 200 caracteres tras recortar espacios.
- precio de costo: decimal requerido, hasta 2 decimales, |x| ≤ ImporteMaximo; admite 0 y negativos.
- margen: decimal requerido, 0 ≤ x ≤ 1000, hasta 2 decimales.

**Error handling**
- Un campo inválido al evaluar — la política devuelve la infracción del campo; no lanza.
- Un valor inválido al construir la entidad — `ArgumentException` con el nombre del parámetro y sin el valor.
- Un precio de venta que no entra en el tipo — `PrecioVentaFueraDeRango`, nunca un desborde.

**Required tests**
- [ ] `Calcular_RI_Costo1210Margen50_Desglose1000_1500_Venta1815` — validates AC-07
- [ ] `Calcular_RI_Costo1000Margen50_Venta1500` — validates AC-08
- [ ] `Calcular_RI_Costo1210Margen150_Venta3025` — validates AC-04
- [ ] `Calcular_RI_CostoMenos100Margen50_VentaMenos150` — validates AC-06 (regresión de ADR-010)
- [ ] `Calcular_Monotributo_Costo1210Margen50_Venta1815` — validates AC-09
- [ ] `Calcular_Multiplo50_1815coma37_Redondea1850` — validates AC-10
- [ ] `Calcular_Multiplo0coma01_1666coma656_Redondea1666coma66` — validates AC-11
- [ ] `Calcular_CasosMedidosPorLaAuditoria_SinCentavoDeMas` — 3 y 50 % con alícuota 27 da 4,50; 33,33 y 0 % da 33,33
- [ ] `Calcular_Negativo_RedondeaHaciaMasInfinito` — D-04
- [ ] `Calcular_PrecioQueNoEntra_Rechazado` — `PrecioVentaFueraDeRango` (D-06) (caso de error)
- [ ] `Evaluar_CodigoFaltante_Rechazado` y `Evaluar_DescripcionFaltante_Rechazado` — vacío y solo espacios; validates AC-03 (caso de error)
- [ ] `Evaluar_Codigo51YDescripcion201_Rechazados` — D-02 (caso de error)
- [ ] `Evaluar_MargenMenos1_Rechazado` y `Evaluar_Margen1000coma01_RechazadoPorRango` — validates AC-05 (caso de error)
- [ ] `Evaluar_MargenBordes0y1000_Validos`
- [ ] `Evaluar_CostoConTresDecimales_Invalido` y `Evaluar_MargenConTresDecimales_Invalido` — D-03 (caso de error)
- [ ] `Evaluar_CostoCeroYNegativo_Validos` — FR-05
- [ ] `Clave_EspaciosYMayusculas_IgualQueABC1` — " abc-1 " y "ABC-1" dan la misma clave; validates AC-02
- [ ] `Clave_EspacioDuroYTabulador_IgualQueABC1` — "ABC-1" seguido de NBSP o precedido de tabulador da la misma clave (D-13)
- [ ] `Evaluar_SoloEspaciosDuros_RechazadoComoFaltante` — un código hecho solo de NBSP es inválido por faltante
- [ ] `Articulo_CostoNoPositivo_Advertencia` — validates AC-06
- [ ] `Articulo_ValorInvalido_LanzaSinIncluirElValor` — error sin el valor en el mensaje
- [ ] `ReglasImporte_MismasRespuestasQueAntes` — `PoliticaConfiguracionNegocioTests` sigue en verde sin cambios

**Completion criterion**
Las pruebas del bloque pasan con `dotnet test tests/Optica.Domain.Tests`, las de configuración siguen en verde sin
cambios y `Optica.Domain` sigue sin referencias a otros proyectos.

## Block 2 — Infraestructura: tabla `Articulo`, migración y repositorio

**Files**
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (modified) — `DbSet<ArticuloFila>`, mapeo con el
  conversor `Centesimos` existente, CHECKs, índice UNIQUE y la clase `ArticuloFila`.
- `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs` (new)
- `src/Optica.Infrastructure/Persistencia/Migraciones/<fecha>_Catalogo.cs` y `.Designer.cs` (new)
- `src/Optica.Infrastructure/Persistencia/Migraciones/OpticaDbContextModelSnapshot.cs` (modified)
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registra `IArticuloRepository` (scoped).
- `tests/Optica.Infrastructure.Tests/Persistencia/ArticuloRepositoryTests.cs` (new)
- `tests/Optica.Infrastructure.Tests/Acceso/AccesoPersistenciaTests.cs` (modified) — la lista exacta de migraciones
  suma `Catalogo` y la reversión a `Inicial` también quita y recrea `Articulo`.

**Logic**
`ArticuloFila`: `long Id`, `string CodigoProveedor`, `string CodigoProveedorClave`, `string Descripcion`,
`decimal PrecioCosto`, `decimal MargenUtilidad`, `decimal PrecioVenta`, mapeados a centésimos con `Centesimos`
(ADR-008, que ya rechaza más de 2 decimales en lugar de truncar). `Id` autonumérico con AUTOINCREMENT (no se
reutilizan números: no hay eliminación definitiva). `Agregar` registra la fila; `ExisteClaveAsync` y `ObtenerAsync`
leen con `AsNoTracking`; `ObtenerPorClaveAsync` busca por `CodigoProveedorClave`, que es como el caso de uso lee el
código asignado después de confirmar (si devuelve null es estado corrupto: `InvalidOperationException`, 500
genérico, como D-04 de FEAT-001c). Una fila que no cumple la política al leerla se informa como
`InvalidOperationException("El artículo guardado no es válido.")`.

**Data model**
- Tabla `Articulo`: `Id` INTEGER PRIMARY KEY AUTOINCREMENT; `CodigoProveedor` TEXT NOT NULL,
  `CHECK (length(CodigoProveedor) BETWEEN 1 AND 50)` (`CK_Articulo_CodigoProveedor`); `CodigoProveedorClave` TEXT
  NOT NULL, índice UNIQUE `IX_Articulo_CodigoProveedorClave`; `Descripcion` TEXT NOT NULL,
  `CHECK (length(Descripcion) BETWEEN 1 AND 200)` (`CK_Articulo_Descripcion`); `PrecioCostoCentavos` INTEGER NOT
  NULL; `MargenCentesimos` INTEGER NOT NULL, `CHECK BETWEEN 0 AND 100000` (`CK_Articulo_Margen`);
  `PrecioVentaCentavos` INTEGER NOT NULL. Sin columna de activo (D-08).

**Input validation**
- Los valores llegan como `Articulo` ya validado por Block 1; la base vuelve a controlar largos, margen y unicidad.

**Error handling**
- Clave duplicada al confirmar — SQLite 2067 → `ConflictoAlConfirmarException` (ya lo hace `UnidadDeTrabajoEf`), sin
  causa adjunta.
- Un CHECK violado — `DbUpdateException` propagada como error, no como conflicto.
- Fila corrupta al leer — `InvalidOperationException` sin el valor.

**Required tests**
- [ ] `Agregar_YConfirmar_AsignaCodigoAutonumerico` — dos altas dan códigos consecutivos; validates AC-01
- [ ] `Agregar_PrecioVenta_GuardadoEnCentavos` — 1815,00 queda 181500; validates AC-07
- [ ] `Agregar_ClaveDuplicada_RechazadoConConflicto` — la segunda alta con " abc-1 " da `ConflictoAlConfirmarException`; validates AC-02
- [ ] `ExisteClave_EncuentraSinDistinguirMayusculas`
- [ ] `Insertar_MargenFueraDeRango_RechazadoPorCheck` — SQL directo con 100001 falla (caso de error)
- [ ] `Insertar_CodigoVacioODe51_RechazadoPorCheck` (caso de error)
- [ ] `Obtener_FilaInvalida_LanzaErrorDeEstadoCorrupto` (caso de error)
- [ ] `AgregarInfraestructura_RegistraRepositorioDeArticulos`
- [ ] `Migracion_IndiceTipoResultado_CreadoYRevertible` actualizada con las cinco migraciones y la tabla `Articulo`

**Completion criterion**
Las pruebas del bloque pasan contra una base temporal y `dotnet ef migrations has-pending-model-changes` no informa
cambios pendientes.

## Block 3 — Aplicación: alta y consulta de artículos

**Files**
- `src/Optica.Application/Comun/AccionesNumericas.cs` (new) — "Ingresá hasta 2 decimales." e "Ingresá un valor menor." (D-12).
- `src/Optica.Application/Configuracion/ValidacionConfiguracion.cs` (modified) — usa `AccionesNumericas` sin cambiar textos.
- `src/Optica.Application/Catalogo/ValidacionArticulo.cs` (new) — campos y acciones; traduce infracciones.
- `src/Optica.Application/Catalogo/AltaArticulo.cs` (new)
- `src/Optica.Application/Catalogo/ObtenerArticulo.cs` (new)
- `tests/Optica.Application.Tests/Catalogo/Dobles.cs` (new)
- `tests/Optica.Application.Tests/Catalogo/AltaArticuloTests.cs` (new)
- `tests/Optica.Application.Tests/Catalogo/ObtenerArticuloTests.cs` (new)
- `tests/Optica.Application.Tests/Catalogo/ValidacionArticuloTests.cs` (new)

**Logic**
Campos: `codigoProveedor`, `descripcion`, `precioCosto`, `margenUtilidad`. Acciones (texto fijo, sin el valor):
código faltante "Completá el código en el proveedor."; código largo "Usá hasta 50 caracteres."; descripción faltante
"Completá la descripción."; descripción larga "Usá hasta 200 caracteres."; costo faltante "Ingresá el precio de
costo."; margen faltante "Ingresá el margen de utilidad."; margen fuera de rango "El margen debe estar entre 0 y
1.000." (AC-05); escala `AccionesNumericas.HastaDosDecimales`; rango del tipo `AccionesNumericas.ValorMenor`; precio de
venta fuera de rango en `margenUtilidad` (D-06); duplicado en `codigoProveedor` "Ese código ya pertenece a otro
artículo." (AC-02, D-07). Advertencia: "Precio de costo negativo o cero" (FR-05).
`AltaArticulo.EjecutarAsync(DatosArticulo)` devuelve `ResultadoAlta` (propio del módulo, no el `Resultado` común):
`Grabado(ArticuloGrabado)` o `Invalido(IReadOnlyList<ErrorValidacion>)`. En orden: evalúa la política (todos los
errores, sin tocar la base); lee la configuración vigente con `IConfiguracionNegocioRepository.ObtenerAsync` en cada
llamada (FR-08); calcula (si da `PrecioVentaFueraDeRango`, error en `margenUtilidad`); si `ExisteClaveAsync` → error
de duplicado; si no, `Agregar`, `ConfirmarAsync` y `ObtenerPorClaveAsync` para leer el código asignado; una `ConflictoAlConfirmarException` se traduce al mismo error de
duplicado (D-07: en el alta de un artículo solo puede venir del índice UNIQUE). Devuelve `ArticuloGrabado(long
Codigo, string CodigoProveedor, string Descripcion, decimal PrecioCosto, decimal MargenUtilidad, decimal PrecioVenta,
string? Advertencia)`.
`ObtenerArticulo.EjecutarAsync(long codigo)` devuelve el `ArticuloGrabado` o null.

**Data model**
- Reuses `Articulo` from Block 2 — no schema change; constraints as declared there: `Id` AUTOINCREMENT, largos de
  código (1 a 50) y descripción (1 a 200), margen 0 a 100000 centésimos, UNIQUE sobre `CodigoProveedorClave`.

**Input validation**
- `DatosArticulo(string? CodigoProveedor, string? Descripcion, decimal? PrecioCosto, decimal? MargenUtilidad)`: las
  reglas de Block 1, una infracción por campo.

**Error handling**
- Datos inválidos — `Invalido` con un error por campo; no lee la configuración ni graba.
- Código duplicado (chequeo previo o `ConflictoAlConfirmarException`) — `Invalido` con el error en `codigoProveedor`.
- Precio de venta fuera de rango — `Invalido` con el error en `margenUtilidad`.
- Sin fila de configuración — la `InvalidOperationException` de FEAT-001c se propaga (D-04 de FEAT-001c).

**Required tests**
- [ ] `Alta_Valida_DevuelveCodigoYPrecio` — validates AC-01
- [ ] `Alta_CodigoDuplicadoConEspaciosYMinusculas_Rechazado` — validates AC-02 (caso de error)
- [ ] `Alta_ConflictoAlConfirmar_RechazadoComoDuplicado` — D-07 (caso de error)
- [ ] `Alta_SinCodigoOSinDescripcion_RechazadoSinGrabar` — validates AC-03 (caso de error)
- [ ] `Alta_Margen1000coma01_RechazadoConRango` — validates AC-05 (caso de error)
- [ ] `Alta_CostoNegativo_GrabaConAdvertencia` — validates AC-06
- [ ] `Alta_UsaLaConfiguracionVigente_SinCache` — cambiar la configuración del doble entre dos altas cambia el precio; validates AC-04, AC-07
- [ ] `Alta_Monotributo_PrecioSinDescontarIva` — validates AC-09
- [ ] `Alta_PrecioFueraDeRango_Rechazado` — D-06 (caso de error)
- [ ] `Alta_VariosCamposInvalidos_DevuelveTodosLosErrores` (caso de error)
- [ ] `Alta_SinConfiguracion_PropagaError` (caso de error)
- [ ] `Obtener_Inexistente_DevuelveNull` — código inexistente, sin error
- [ ] `Traducir_CadaInfraccion_CampoYAccionFijos` — cada valor del enum tiene traducción; uno no definido falla con error

**Completion criterion**
Las pruebas del bloque pasan con dobles en memoria, las de configuración siguen en verde, `Optica.Application` sigue
sin depender de EF Core y `Optica.Application.Catalogo` no usa el namespace `Optica.Application.Configuracion`.

## Block 4 — Api: `POST /api/articulos` y `GET /api/articulos/{codigo}`

**Files**
- `src/Optica.Api/Controllers/ArticulosController.cs` (new)
- `src/Optica.Api/Program.cs` (modified) — registra `AltaArticulo` y `ObtenerArticulo` (scoped).
- `tests/Optica.Api.Tests/Catalogo/ArticulosApiTests.cs` (new)
- `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs` (modified) — las rutas con parámetros se piden con un valor de
  ejemplo (`{codigo}` → `1`) y siguen exigiendo 401 sin sesión; ninguna ruta se excluye.
- `tests/Optica.Api.Tests/Acceso/SinDatosArcaTests.cs` (modified) — graba un artículo antes de recorrer los GET, pide
  `/api/articulos/{codigo}` con su código y afirma que la ruta quedó en `conJson`.

**Logic**
El controlador solo traduce HTTP, sin `[AllowAnonymous]` (R-01). `POST` con `[RequestSizeLimit(1024)]` y números JSON
estrictos (`decimal?`), textos como `string?`; cuerpo ilegible → 400 con un error en el campo que nombra
`ModelState` (o `codigoProveedor` si ninguno) y
`RespuestasValidacion.AccionCuerpoIlegibleConfiguracion` (texto genérico, ya existente); `Invalido` → 400 con todos
los campos; `Grabado` → 201 con `CreatedAtAction` hacia el GET. `GET {codigo:long}` → 200 o 404 con ProblemDetails sin
detalles. `ResponderAltaAsync` es `internal` y cerrado por defecto (un resultado no previsto lanza → 500).

**API contract**
- Method + path: `POST /api/articulos`
- Request: `{ codigoProveedor: string, descripcion: string, precioCosto: number, margenUtilidad: number }`,
  `Content-Type: application/json`, hasta 1024 bytes.
- Response: 201 `{ codigo: number, codigoProveedor: string, descripcion: string, precioCosto: number,
  margenUtilidad: number, precioVenta: number, advertencia: string | null }` y `Location: /api/articulos/{codigo}`.
- Error codes: 400 ValidationProblemDetails con `errors.{campo}`; 401; 413; 415; 500 genérico.
- Auth: sesión de FEAT-001a (política por defecto).
- Method + path: `GET /api/articulos/{codigo:long}`
- Request: sin cuerpo.
- Response: 200 con el mismo cuerpo que el 201.
- Error codes: 401; 404 ProblemDetails sin detalles; 500 genérico.
- Auth: sesión de FEAT-001a.

**Input validation**
- Cuerpo hasta 1024 bytes, JSON obligatorio, números estrictos; `codigo` de ruta entero de 64 bits; el resto lo valida Block 3.

**Error handling**
- Sin sesión — 401 antes de leer el cuerpo.
- Content-Type que no es JSON — 415.
- Cuerpo mayor a 1024 bytes — 413.
- Cuerpo ilegible — 400 con un error genérico, sin detalles del lector de JSON ni el valor.
- Datos inválidos o código duplicado — 400 con los campos.
- Artículo inexistente — 404 sin detalles.

**Required tests**
- [ ] `Post_Valido_201ConCodigoYPrecio` — y el GET de `Location` devuelve lo mismo; validates AC-01, AC-07
- [ ] `Post_CodigoDuplicado_Rechazado400EnElCampo` — " abc-1 " contra "ABC-1"; validates AC-02 (caso de error)
- [ ] `Post_SinDescripcion_Rechazado400EnElCampo` — validates AC-03 (caso de error)
- [ ] `Post_Margen1000coma01_Rechazado400EnElCampo` — validates AC-05 (caso de error)
- [ ] `Post_CostoNegativo_201ConAdvertencia` — validates AC-06
- [ ] `Post_Monotributo_201Precio1815` — configuración cambiada por `POST /api/configuracion`; validates AC-09
- [ ] `Post_Multiplo50_201Precio1850` — validates AC-10
- [ ] `PostYGet_SinSesion_NoAutorizado401` — sin sesión (caso de error)
- [ ] `Post_SinContentTypeJson_Rechazado415` (caso de error)
- [ ] `Post_CuerpoGrande_Rechazado413` (caso de error)
- [ ] `Post_CuerpoIlegible_Invalido400SinDetallesInternos` — JSON roto y texto en un número (caso de error)
- [ ] `Get_Inexistente_Error404SinDetalles` — no encontrado (caso de error)
- [ ] `ResponderAlta_ResultadoNoPrevisto_Error500` — cerrado por defecto (caso de error)
- [ ] `EndpointsProtegidos_SinSesion_NoAutorizado401` actualizada — incluye `GET /api/articulos/1` sin excluir rutas (caso de error)
- [ ] `SinDatosArcaTests` actualizada — incluye `/api/articulos/{codigo}` en `conJson`

**Completion criterion**
Las pruebas del bloque pasan y `RutasAnonimas_SonExactamenteCinco` sigue sin cambios.

## Block 5 — Web: pantalla de alta de artículos

**Files**
- `web/src/configuracion/numeros.ts` (modified) — acoplamiento aceptado (D-14): los auxiliares numéricos siguen en
  `configuracion/` hasta que un tercer módulo los use; `mostrarImporte(valor)`: 2 decimales, coma decimal, punto de miles
  ("1.850,00", "-150,00") (D-11).
- `web/src/configuracion/numeros.test.ts` (modified)
- `web/src/catalogo/reglas.ts` (new) — reglas de Block 1 y textos de Block 3, con el mismo recorte de espacios
  (`trim()` de JavaScript, todo espacio en blanco, D-13); reutiliza `MENSAJE_HASTA_DOS_DECIMALES`,
  `MENSAJE_VALOR_MENOR`, `MENSAJE_NO_ES_NUMERO` e `IMPORTE_MAXIMO_SEGURO` de `web/src/configuracion/reglas.ts`; el
  rango del costo se controla en los dos sentidos.
- `web/src/catalogo/reglas.test.ts` (new)
- `web/src/api/cliente.ts` (modified) — `grabarArticulo` (todos los errores del 400 con `todosLosErrores`, 413 con
  mensaje propio general, 409 no se usa) y `consultarArticulo`; no cambia lo de acceso ni de configuración.
- `web/src/api/cliente.test.ts` (modified)
- `web/src/paginas/NuevoArticulo.tsx` (new)
- `web/src/paginas/NuevoArticulo.test.tsx` (new)
- `web/src/App.tsx` (modified) — `<Route path="/articulos/nuevo"` literal.
- `web/src/App.test.tsx` (modified) — `PANTALLAS` suma `/articulos/nuevo`.
- `web/src/paginas/Inicio.tsx` (modified) — enlace "Nuevo artículo".
- `web/src/paginas/Inicio.test.tsx` (modified)

**Logic**
Al montarse verifica la sesión (401 → `/ingreso`). Campos con etiqueta: "Código en el proveedor", "Descripción",
"Precio de costo ($)", "Margen de utilidad (%)" y "Precio de venta ($)" de solo lectura (`readOnly`, vacío hasta
grabar; FR-11). Valida con `catalogo/reglas.ts` y no envía si hay errores; cada error junto a su campo
(`aria-invalid`, `aria-describedby`); los del servidor también. Con 201 muestra "Artículo grabado con el código N.",
el Precio de Venta con `mostrarImporte`, y si viene, la advertencia "Precio de costo negativo o cero" con
`role="status"`; el formulario queda listo para otra alta. Sin consultas periódicas; los valores viven solo en el
estado del componente.

**API contract**
- Method + path: consume `POST /api/articulos` y `GET /api/sesion`, sin cambiarlos.
- Request: `{ codigoProveedor, descripcion, precioCosto, margenUtilidad }`.
- Response: 201 con el artículo grabado.
- Error codes: 400 con `errors.{campo}`, 401, 413, 429, 5xx.
- Auth: cookie de sesión del navegador.

**Input validation**
- Textos: `maxLength` 50 y 200 (además de la regla tras recortar); números: hasta 32 caracteres, formato de
  `leerDecimal` (coma o punto decimal, signo menos permitido en el costo, sin miles), reglas de Block 1.

**Error handling**
- Texto que no es un número — "Ingresá un número." junto al campo; no envía.
- Regla violada — el mensaje de Block 3 junto al campo; no envía.
- 400 del servidor — cada mensaje junto a su campo; un campo desconocido como mensaje general.
- 401 — navega a `/ingreso`.
- Sin conexión, 413, 429 o 5xx — mensaje general `role="alert"`, conserva lo escrito.

**Required tests**
- [ ] `mostrarImporte_MilesYDecimales` — 1850 → "1.850,00"; -150 → "-150,00"; 1666.66 → "1.666,66"
- [ ] `reglas_Margen1000coma01_Rechazado` — validates AC-05 (caso de error)
- [ ] `reglas_CostoFueraDeRangoEnLosDosSentidos_Rechazado` (caso de error)
- [ ] `NuevoArticulo_Valido_MuestraCodigoYPrecio` — validates AC-01
- [ ] `NuevoArticulo_PrecioDeVenta_SoloLectura` — FR-11
- [ ] `NuevoArticulo_CodigoDuplicado_RechazadoJuntoAlCampo` — 400 del servidor; validates AC-02
- [ ] `NuevoArticulo_SinCodigoOSinDescripcion_RechazadoSinEnviar` — validates AC-03 (caso de error)
- [ ] `NuevoArticulo_Margen1000coma01_RechazadoSinEnviar` — validates AC-05 (caso de error)
- [ ] `NuevoArticulo_CostoNegativo_MuestraAdvertencia` — validates AC-06
- [ ] `NuevoArticulo_NoNumero_Invalido` — "abc" en el costo (caso de error)
- [ ] `NuevoArticulo_SinSesion401_NavegaAlIngreso` — no autorizado
- [ ] `NuevoArticulo_SinConexion_ErrorGeneralConservaValores` — error de conexión, 413, 429 y 500
- [ ] `grabarArticulo_400_DevuelveTodosLosErrores` — en `cliente.test.ts` (caso de error)
- [ ] `Inicio_EnlaceNuevoArticulo` — en `Inicio.test.tsx`

**Completion criterion**
`npm run test:coverage` pasa con el umbral de 80 % y `npm run build` compila sin errores.

## Block 6 — E2E: AC-01 a AC-11 en Chrome y Edge

**Files**
- `e2e/tests/articulos.spec.ts` (new) — proyectos `chrome-articulos` y `msedge-articulos`, ya derivados de `tests/`.
- `e2e/README.md` (modified) — tabla de proyectos, puertos y bases con `articulos`.

**Logic**
Con su base nueva, en serie: define la contraseña e ingresa; con la configuración inicial (RI, 21, 0,01) cubre AC-01,
AC-07, AC-08, AC-04, AC-11 (costo y margen que dan 1.666,656), AC-02, AC-03, AC-05 y AC-06; después cambia a múltiplo 50
desde la pantalla de configuración para AC-10 y vuelve a 0,01; después cambia a Monotributo para AC-09. Los números se
escriben como la operadora ("1210", "1000,01", "-100").

**Data model**
- Reuses `Articulo` from Block 2 — no schema change; constraints as declared there: NOT NULL en todas las columnas,
  `Id` AUTOINCREMENT, UNIQUE sobre `CodigoProveedorClave`, CHECK de largos y de margen.

**Input validation**
- Las entradas de los AC, escritas en los campos ("1000,01", "-100", " abc-1 ").

**Error handling**
- Una Api que no arranca — `servidor.mjs` termina con error y no corren las pruebas (sin cambios).

**Required tests**
- [ ] `articulos: alta asigna código y precio` — validates AC-01
- [ ] `articulos: código duplicado rechazado junto al campo` — validates AC-02
- [ ] `articulos: sin código o sin descripción rechazado` — validates AC-03
- [ ] `articulos: costo 1210 y margen 150 da 3.025,00` — validates AC-04
- [ ] `articulos: margen -1 y 1000,01 rechazado` — validates AC-05
- [ ] `articulos: costo -100 graba -150,00 con advertencia` — validates AC-06
- [ ] `articulos: costo 1210 y margen 50 da 1.815,00` — validates AC-07
- [ ] `articulos: costo 1000 y margen 50 da 1.500,00` — validates AC-08
- [ ] `articulos: Monotributo da 1.815,00` — validates AC-09
- [ ] `articulos: múltiplo 50 redondea a 1.850,00` — validates AC-10
- [ ] `articulos: 1.666,656 redondea a 1.666,66` — validates AC-11

**Completion criterion**
`npm run test:unidad && npm run test:harness` pasa en los proyectos de Chrome y Edge de los tres archivos.

## Final verification
`dotnet build Optica.sln` y `dotnet test Optica.sln` pasan; en `web/`, `npm run build` y `npm run test:coverage`; en
`e2e/`, `npm run test:unidad && npm run test:harness` en Chrome y Edge; `validate_spec.py --tier FEATURE` pasa sobre
este documento.
