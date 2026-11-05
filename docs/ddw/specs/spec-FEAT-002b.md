# Spec FEAT-002b: Búsqueda, edición y baja de artículos

| Field | Value |
|-------|-------|
| Ticket | FEAT-002b |
| PRD | docs/ddw/prd/prd-FEAT-002b.md |
| Tier | FEATURE |
| Date | 2026-11-04 |
| Spec loops | 3 |
| Loops since last human decision | 1 |

## Summary
Sobre el catálogo de FEAT-002a, el dominio suma a `Articulo` el estado activo, una versión y la regla de
modificación: un inactivo no se modifica (D-02) y el precio de venta se recalcula solo si cambia el costo o el margen
(D-03). Un plegado de texto del dominio (D-04) se registra como función `plegar` en cada conexión SQLite (ADR-011)
para buscar sin mayúsculas ni acentos, paginado de a 50 por descripción (D-01). La migración `CatalogoActivo` agrega
las columnas `Activo` y `Version`; la aplicación suma los casos de uso de búsqueda, modificación y cambio de estado;
la Api expone `GET /api/articulos`, `PUT /api/articulos/{codigo}` y `PUT /api/articulos/{codigo}/estado` con sesión;
la SPA suma el listado `/articulos` y la edición `/articulos/{codigo}`. Decisiones:
`docs/ddw/specs/decisions-FEAT-002b.md` (D-01 a D-21).

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 3, Block 4, Block 5, Block 6 |
| FR-02 | Block 4, Block 5, Block 6 |
| FR-03 | Block 1, Block 3, Block 6 |
| FR-04 | Block 1, Block 2, Block 3, Block 4, Block 5, Block 6 |
| FR-05 | Block 1, Block 2, Block 3, Block 4, Block 5, Block 6 |
| FR-06 | Block 1, Block 3, Block 4, Block 5, Block 6 |
| FR-07 | Block 2, Block 4, Block 5, Block 6 |
| NFR-01 | Strategy: Block 6 corre AC-01 a AC-06 en los proyectos `chrome-articulos-edicion` y `msedge-articulos-edicion` (canal estable instalado); la versión anterior de cada navegador se verifica a mano, como en FEAT-001 y FEAT-002a |
| NFR-02 | Strategy: importes en centésimos enteros (ADR-008) sin cambios; la Api devuelve los importes con 2 decimales y la pantalla los formatea con `mostrarImporte` de FEAT-002a |

## Dependencies between blocks
Block 1 → Block 2 → Block 3 → Block 4 → Block 5 → Block 6. Cada bloque usa los tipos del anterior.

## Block 1 — Dominio: estado, versión, modificación y plegado de búsqueda

**Files**
- `src/Optica.Domain/Catalogo/Articulo.cs` (modified) — `Activo` y `Version` obligatorios en el constructor de reconstrucción; fábrica `Nuevo`; métodos `Modificar` y `ConEstado`.
- `src/Optica.Domain/Catalogo/ResultadoModificacion.cs` (new) — resultado de `Modificar`: `Modificado` con el artículo, `Invalido` con la lista de infracciones o `Inactivo` (D-20); `InfraccionArticulo` (en `PoliticaArticulo.cs`) no cambia.
- `src/Optica.Domain/Catalogo/PlegadoBusqueda.cs` (new) — `Plegar(string)` (D-04) y `ClaveOrden(string)` (D-15).
- `src/Optica.Domain/Catalogo/PaginaArticulos.cs` (new) — `Items` y `Total` de una búsqueda.
- `src/Optica.Domain/Catalogo/IArticuloRepository.cs` (modified) — `BuscarAsync`, `ExisteClaveDeOtroAsync`, `Actualizar`, `CambiarEstado`.
- `src/Optica.Application/Catalogo/AltaArticulo.cs` (modified) — usa `Articulo.Nuevo` (única línea).
- `tests/Optica.Domain.Tests/Catalogo/ArticuloTests.cs` (modified) — llamadas con `Activo`/`Version` y casos nuevos.
- `tests/Optica.Domain.Tests/Catalogo/PlegadoBusquedaTests.cs` (new)

**Logic**
- `new Articulo(codigo, codigoProveedor, descripcion, costo, margen, precioVenta, activo, version)`: `activo` y
  `version` (long ≥ 0) son obligatorios (D-10); `Articulo.Nuevo(codigoProveedor, descripcion, costo, margen,
  precioVenta)` crea uno sin código, activo y con versión 0.
- `Modificar(string? codigoProveedor, string? descripcion, decimal? costo, decimal? margen, ConfiguracionNegocio
  vigente)` → `ResultadoModificacion`. Si `Activo` es falso devuelve `Inactivo` sin evaluar nada más (D-02, D-20).
  Si no, evalúa `PoliticaArticulo` y devuelve `Invalido` con la lista completa de infracciones, una por campo, como
  el alta. Si costo y margen son iguales a los guardados conserva `PrecioVenta`; si alguno cambió calcula con
  `CalculadoraPrecioVenta` y su infracción de rango va en la lista (D-03). Si no hay infracciones devuelve
  `Modificado` con una copia con el mismo código, la misma versión y `Activo` verdadero; la versión la incrementa la
  persistencia.
- `ConEstado(bool activo)` devuelve una copia que solo cambia `Activo` (D-09).
- `PlegadoBusqueda.Plegar(texto)`: recorte de D-17 de FEAT-002a, normalización NFD, quita todas las marcas
  `NonSpacingMark` salvo U+0303 cuando sigue a una "n" o "N", de modo que "ñ" se recompone y "ã" queda "a";
  normaliza a NFC y pasa a minúsculas invariantes (D-04). Con UTF-16 inválido devuelve el texto en minúsculas sin
  normalizar, sin lanzar (ADR-011).
- `PlegadoBusqueda.ClaveOrden(texto)`: `Plegar(texto)` con cada "ñ" reemplazada por "n" seguida de U+007B ("{"),
  que en orden binario queda después de cualquier letra ASCII minúscula y antes de la letra siguiente a la n: así
  "nube" < "ñandú" < "ojo" (D-15).
- `IArticuloRepository`: `BuscarAsync(string? textoPlegado, int pagina, int tamano)` → `PaginaArticulos`;
  `ExisteClaveDeOtroAsync(string clave, long codigo)`; `Actualizar(Articulo articulo, long versionLeida)` registra
  la modificación; `CambiarEstado(long codigo, bool activo)` registra el cambio de estado. Ambas se confirman con la
  unidad de trabajo.

**Data model**
- Entidad `Articulo` del dominio, sin tabla propia en este bloque: `Activo` bool NOT NULL, `Version` long NOT NULL
  (≥ 0, default 0 en `Nuevo`); los demás campos con las restricciones de `PoliticaArticulo`. Block 2 las persiste con
  CHECK y DEFAULT.

**Input validation**
- Los cuatro datos de `Modificar`: las reglas de `PoliticaArticulo` de FEAT-002a sin cambios (largos 50 y 200 tras el
  recorte, UTF-16 válido, costo y margen con hasta 2 decimales, margen de 0 a 1.000).
- `version`: entero ≥ 0.

**Error handling**
- Artículo inactivo — `Modificar` devuelve `ResultadoModificacion.Inactivo` y no evalúa los datos (D-02, D-20).
- Datos inválidos — `Modificar` devuelve las infracciones de la política, una por campo.
- Precio de venta fuera de rango — infracción de la calculadora (D-06 de FEAT-002a).
- Versión negativa en el constructor — `ArgumentException` con el nombre del campo, sin el valor.

**Required tests**
- [ ] `Modificar_InactivoRechazado_DevuelveResultadoInactivo` — inactivo rechazado sin evaluar los datos (D-02)
- [ ] `Modificar_DatosInvalidos_DevuelveTodasLasInfracciones` — datos inválidos rechazados, uno por campo
- [ ] `Modificar_PrecioFueraDeRango_DevuelveInfraccionDeRango` — error de rango, no desborde
- [ ] `Constructor_VersionNegativaInvalida_Lanza` — versión inválida rechazada
- [ ] `Modificar_MargenDe50A60_Recalcula1936` — validates AC-02
- [ ] `Modificar_CostoDe1210A2420_Recalcula3630` — validates AC-03
- [ ] `Modificar_SoloDescripcion_ConservaPrecioVenta` — D-03
- [ ] `ConEstado_Desactivar_ConservaLosDatos` — validates AC-05
- [ ] `ConEstado_Reactivar_QuedaActivo` — validates AC-06
- [ ] `Nuevo_QuedaActivoConVersionCero` — D-10
- [ ] `Plegar_Armazon_IgualQueArmazonSinTilde` — validates AC-04
- [ ] `Plegar_Enie_NoEquivaleAEne` — D-04
- [ ] `Plegar_Utf16Invalido_NoLanza` — texto inválido rechazado por la normalización, sin excepción (ADR-011)
- [ ] `Plegar_TildeGraveYDieresis_SeIgnoran` — D-04 ("cafe" = "café" = "cafè", "motorhead" = "Motörhead")
- [ ] `ClaveOrden_Enie_EntreNyO` — D-15

**Completion criterion**
Las 15 pruebas pasan, `Optica.Domain.Tests` queda en verde y `AltaArticulo` compila con `Articulo.Nuevo` sin cambiar
su comportamiento.

## Block 2 — Infraestructura: columnas `Activo` y `Version`, función `plegar` y repositorio

**Files**
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (modified) — `ArticuloFila.Activo` y `ArticuloFila.Version` (token de concurrencia); `HasDbFunction` hacia `FuncionesSqlite.Plegar` (`plegar`) y `FuncionesSqlite.OrdenArticulo` (`orden_articulo`).
- `src/Optica.Infrastructure/Persistencia/FuncionesSqlite.cs` (new) — métodos traducibles `Plegar` y `OrdenArticulo` (lanzan si se evalúan en el cliente).
- `src/Optica.Infrastructure/Persistencia/FabricaConexion.cs` (modified) — `PragmasAlAbrir` registra `plegar` y `orden_articulo` con `CreateFunction(..., isDeterministic: true)` en `ConnectionOpened` y `ConnectionOpenedAsync`.
- `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs` (modified) — `BuscarAsync`, `ExisteClaveDeOtroAsync`, `Actualizar`, `CambiarEstado`; `Agregar` y `AEntidad` con `Activo` y `Version`.
- `src/Optica.Infrastructure/Persistencia/UnidadDeTrabajoEf.cs` (modified) — fija el motivo de `ConflictoAlConfirmarException` (D-08).
- `src/Optica.Application/Comun/IUnidadDeTrabajo.cs` (modified) — `ConflictoAlConfirmarException.Motivo` (`Concurrencia` o `Unicidad`), un solo constructor `(string mensaje, MotivoConflicto motivo)`, sin causa (D-18); documentación de `IUnidadDeTrabajo`.
- `tests/Optica.Application.Tests/Acceso/Dobles.cs`, `Acceso/DefinirContrasenaTests.cs`, `Configuracion/GrabarConfiguracionNegocioTests.cs`, `Catalogo/AltaArticuloTests.cs` y `Comun/ConflictoAlConfirmarExceptionTests.cs` (modified) — construyen la excepción con su motivo; `ConflictoAlConfirmar_NoAdmiteCausa_NingunConstructorRecibeExcepcion` sigue pasando.
- `src/Optica.Infrastructure/Persistencia/Migraciones/{timestamp}_CatalogoActivo.cs` y `.Designer.cs` (new), `OpticaDbContextModelSnapshot.cs` (modified).
- `tests/Optica.Infrastructure.Tests/Persistencia/ArticuloRepositoryTests.cs` (modified)
- `tests/Optica.Infrastructure.Tests/Persistencia/FabricaConexionTests.cs` (modified)
- `tests/Optica.Infrastructure.Tests/Acceso/AccesoPersistenciaTests.cs` (modified) — lista exacta de migraciones con `CatalogoActivo`; `Version` es token.

**Logic**
- `ArticuloRepository.BuscarAsync`: sin texto, todo el catálogo; con texto, las filas donde
  `instr(plegar(Descripcion), @t) > 0 OR instr(plegar(CodigoProveedor), @t) > 0 OR instr(CAST(Id AS TEXT), @t) > 0`
  (D-05, D-06). Orden `orden_articulo(Descripcion)` (D-15), luego `Id`; el desplazamiento se calcula en `long`
  como `(pagina - 1) * tamano`, se verifica que no pase de `int.MaxValue` (si pasa, `ArgumentOutOfRangeException`)
  y se pasa a `Skip` como `int`, con `Take(tamano)` (D-21); `Total` con `CountAsync` sobre el mismo filtro.
  Consultas parametrizadas de EF Core.
- `ExisteClaveDeOtroAsync`: `AnyAsync(a => a.CodigoProveedorClave == clave && a.Id != codigo)`.
- `Actualizar(articulo, versionLeida)`: con el patrón de `CredencialAccesoRepository.Reemplazar`, reutiliza la
  entrada que el contexto ya sigue (`Local.FindEntry`) o adjunta la fila; asigna `versionLeida` como valor original
  de `Version` e ignora `articulo.Version` (D-17); marca modificadas solo `CodigoProveedor`, `CodigoProveedorClave`,
  `Descripcion`, `PrecioCosto`, `MargenUtilidad`, `PrecioVenta` y `Version` (= versionLeida + 1).
- `Agregar` asigna siempre `Activo` y `Version`; los DEFAULT viven solo en la migración, sin `HasDefaultValue`
  (D-21).
- `CambiarEstado(codigo, activo)`: lee la fila con seguimiento y modifica solo `Activo` y `Version` (+1) (D-09).
- `UnidadDeTrabajoEf`: `DbUpdateConcurrencyException` → motivo `Concurrencia`; todo lo que hoy reconoce
  `EsConflicto` (índices UNIQUE, clave primaria y CHECK de la credencial) → `Unicidad` (D-18); mensajes y descarte
  del contexto sin cambios.
- `plegar` y `orden_articulo` registradas: nulo devuelve nulo; delegan en `PlegadoBusqueda.Plegar` y
  `PlegadoBusqueda.ClaveOrden`. Nunca se usan en objetos del esquema (ADR-011).

**Data model**
- Tabla `Articulo` (FEAT-002a), sin cambios en las columnas existentes ni en sus CHECK, el índice UNIQUE de
  `CodigoProveedorClave` ni el AUTOINCREMENT.
- Columna nueva `Activo`: INTEGER NOT NULL DEFAULT 1, `CHECK ("Activo" IN (0, 1))` (`CK_Articulo_Activo`); las filas
  existentes quedan activas.
- Columna nueva `Version`: INTEGER NOT NULL DEFAULT 0, `CHECK ("Version" >= 0)` (`CK_Articulo_Version`), token de
  concurrencia.
- Sin índices nuevos (la búsqueda parcial recorre la tabla, ADR-011).
- Rollback: el `Down` de `CatalogoActivo` quita las dos columnas y sus CHECK y deja la tabla de FEAT-002a; la prueba
  de revertir y volver a aplicar de `AccesoPersistenciaTests` lo cubre. Revertir pierde el estado inactivo de los
  artículos, no sus datos.

**Input validation**
- `BuscarAsync`: recibe el texto ya validado y plegado por Block 3; `pagina` ≥ 1 y `tamano` > 0, si no
  `ArgumentOutOfRangeException`.
- `ExisteClaveDeOtroAsync` y `Actualizar`: clave no nula; `Actualizar` rechaza un artículo sin código.

**Error handling**
- Fila con `Activo` fuera de 0/1 o `Version` negativa (eludió los CHECK) — `InvalidOperationException` de estado
  corrupto, sin valores.
- `Actualizar` con la versión cambiada — `ConflictoAlConfirmarException` con motivo `Concurrencia` al confirmar.
- `Actualizar` con una clave ya usada por otro — `ConflictoAlConfirmarException` con motivo `Unicidad`.
- `CambiarEstado` de un código inexistente — `InvalidOperationException` (el caso de uso verifica antes la
  existencia).
- Página o tamaño inválido — `ArgumentOutOfRangeException`.

**Required tests**
- [ ] `AEntidad_ActivoInvalido_EsEstadoCorrupto` — fila con `Activo` inválido rechazada como corrupta
- [ ] `Actualizar_VersionCambiada_ConflictoDeConcurrencia` — conflicto de concurrencia
- [ ] `Actualizar_ClaveDuplicada_ConflictoDeUnicidad` — duplicado rechazado con motivo `Unicidad`
- [ ] `CambiarEstado_CodigoInexistente_LanzaError` — error de código inexistente
- [ ] `BuscarAsync_PaginaInvalida_Lanza` — página inválida rechazada
- [ ] `BuscarAsync_Armazon_EncuentraConYSinTilde` — validates AC-04
- [ ] `BuscarAsync_PorcentajeYGuionBajo_NoSonComodines` — D-05
- [ ] `BuscarAsync_PorCodigoYCodigoProveedor_CoincidenciaParcial` — D-06
- [ ] `BuscarAsync_OrdenPorDescripcionYPaginasDe50` — D-01
- [ ] `CambiarEstado_Desactivar_SoloCambiaActivoYVersion` — validates AC-05
- [ ] `CambiarEstado_Reactivar_QuedaActivo` — validates AC-06
- [ ] `Actualizar_GuardaDatosYIncrementaVersion` — validates AC-02
- [ ] `Migracion_CatalogoActivo_DejaActivasLasFilasExistentes` — base de FEAT-002a actualizada
- [ ] `Esquema_Articulo_ColumnasNotNullYChecks` — columnas exactas con `Activo` y `Version`
- [ ] `Plegar_RegistradaEnCadaConexion` — las dos funciones existen en las conexiones de `FabricaConexion`
- [ ] `Esquema_NingunObjetoUsaPlegar` — una conexión cruda, sin las funciones, lee `sqlite_master` y corre
      `PRAGMA integrity_check` sin error, y ningún objeto nombra `plegar` ni `orden_articulo` (ADR-011, R-07)
- [ ] `BuscarAsync_Enie_OrdenaEntreNyO` — D-15
- [ ] `UnidadDeTrabajo_ClavePrimariaYCheck_MotivoUnicidad` — D-18
- [ ] `BuscarAsync_DesplazamientoMayorQueInt_LanzaError` — desplazamiento inválido rechazado

**Completion criterion**
Las 19 pruebas pasan, `HasPendingModelChanges` es falso, la lista de migraciones termina en `CatalogoActivo` y
`Optica.Infrastructure.Tests` queda en verde.

## Block 3 — Aplicación: búsqueda, modificación y cambio de estado

**Files**
- `src/Optica.Application/Catalogo/BuscarArticulos.cs` (new)
- `src/Optica.Application/Catalogo/ModificarArticulo.cs` (new)
- `src/Optica.Application/Catalogo/CambiarEstadoArticulo.cs` (new)
- `src/Optica.Application/Catalogo/ValidacionArticulo.cs` (modified) — errores de búsqueda, página, inactivo y versión.
- `src/Optica.Application/Catalogo/AltaArticulo.cs` (modified) — `ArticuloGrabado` suma `Activo` y `Version`.
- `tests/Optica.Application.Tests/Catalogo/Dobles.cs` (modified) — `ArticulosEnMemoria` implementa los métodos nuevos y simula conflictos de los dos motivos.
- `tests/Optica.Application.Tests/Catalogo/AltaArticuloTests.cs` y `ObtenerArticuloTests.cs` (modified) — `ArticuloGrabado` con 9 campos.
- `tests/Optica.Application.Tests/Catalogo/BuscarArticulosTests.cs`, `ModificarArticuloTests.cs`, `CambiarEstadoArticuloTests.cs` (new)

**Logic**
- `BuscarArticulos.EjecutarAsync(string? texto, int? pagina)`: valida (abajo); texto vacío o ausente → sin filtro;
  pliega con `PlegadoBusqueda`; llama a `BuscarAsync(textoPlegado, pagina ?? 1, 50)` (D-01, D-07); devuelve
  `ResultadoBusqueda` con los artículos (código, código en el proveedor, descripción, precio de venta, activo),
  `Pagina`, `TamanoPagina` = 50 y `Total`. No registra el texto en el log (D-12).
- `ModificarArticulo.EjecutarAsync(long codigo, DatosArticulo datos, long? version)`: valida `version`; lee el
  artículo (inexistente → `NoEncontrado`); si su versión difiere de la enviada → `Inactivo` si el artículo leído
  está inactivo y `Cambiado` si no, sin escribir (D-17; misma precedencia que la relectura tras el conflicto);
  lee la configuración vigente; llama a `Articulo.Modificar`; `Inactivo` → `Inactivo`; `Invalido` → `Invalido` con
  todos los errores; clave de otro artículo → `Invalido` con `CodigoDuplicado`; `Actualizar(articulo, version)`;
  confirma. `ConflictoAlConfirmarException` con
  motivo `Unicidad` → `CodigoDuplicado`; con motivo `Concurrencia` → relee: inactivo → `Inactivo`, inexistente →
  `NoEncontrado`, si no → `Cambiado` (D-08). Grabado → `Grabado` con el artículo releído.
- `CambiarEstadoArticulo.EjecutarAsync(long codigo, bool? activo)`: `activo` ausente → `Invalido`; inexistente →
  `NoEncontrado`; mismo estado → `Grabado` sin escribir (D-09); si no, `CambiarEstado` y confirma.
  `ConflictoAlConfirmarException` con motivo `Concurrencia` → relee: inexistente → `NoEncontrado`, estado ya igual
  al pedido → `Grabado`, si no → `Cambiado` (D-16).

**Data model**
- Reuses `Articulo` from Block 2 — no schema change; constraints as declared there: NOT NULL, CHECK de largos,
  margen, `Activo` IN (0,1) y `Version` ≥ 0, índice UNIQUE de `CodigoProveedorClave`, token `Version`.

**Input validation**
- `texto`: opcional; hasta 200 caracteres después del recorte de D-17; UTF-16 válido.
- `pagina`: opcional (ausente = 1); entero de 1 a 10.000 (D-07).
- `DatosArticulo`: las mismas reglas del alta (`ValidacionArticulo`).
- `version`: obligatoria en la modificación; entero de 0 a 9.007.199.254.740.991 (D-17).
- `activo`: obligatorio en el cambio de estado; booleano.

**Error handling**
- Texto de búsqueda inválido (largo o UTF-16 inválido) — `Invalido` con el error en `texto`, sin buscar.
- Página fuera de rango — `Invalido` con el error en `pagina`: "Pedí una página entre 1 y 10.000." (sin el valor).
- Artículo inexistente — `NoEncontrado`.
- Artículo inactivo — `Inactivo`: "Reactivá el artículo para editarlo." (D-02).
- Datos inválidos o código duplicado — `Invalido` con todos los campos (mismos textos del alta).
- Versión faltante o fuera de rango — `Invalido` con el error en `version`.
- Artículo cambiado por otra pestaña — `Cambiado`: "El artículo cambió mientras lo editabas. Volvé a abrirlo." (D-08).
- Estado faltante — `Invalido` con el error en `activo`.
- Estado cambiado por otra escritura durante la grabación — `Cambiado`: "El artículo cambió. Volvé a cargar el
  listado." (D-16).

**Required tests**
- [ ] `Buscar_TextoInvalido_DevuelveErrorEnTexto` — texto inválido rechazado
- [ ] `Buscar_PaginaFueraDeRango_DevuelveErrorEnPagina` — página inválida rechazada, sin el valor
- [ ] `Modificar_Inexistente_NoEncontrado` — artículo inexistente rechazado
- [ ] `Modificar_Inactivo_Rechazado` — inactivo rechazado sin grabar (D-02)
- [ ] `Modificar_DatosInvalidos_TodosLosErrores` — datos inválidos rechazados
- [ ] `Modificar_CodigoDuplicado_ErrorEnCodigoProveedor` — duplicado rechazado, por chequeo y por conflicto de `Unicidad`
- [ ] `Modificar_VersionFaltante_ErrorEnVersion` — versión faltante rechazada
- [ ] `Modificar_ConflictoDeConcurrencia_Cambiado` — conflicto de concurrencia informado como `Cambiado`, nunca como duplicado
- [ ] `CambiarEstado_ActivoFaltante_ErrorEnActivo` — estado faltante rechazado
- [ ] `CambiarEstado_ConflictoDeConcurrencia_Cambiado` — conflicto informado como `Cambiado`, o `Grabado` si el estado ya es el pedido
- [ ] `Modificar_VersionDistintaDeLaLeida_CambiadoSinEscribir` — conflicto de versión rechazado sin escribir
- [ ] `Modificar_VersionDistintaYDesactivado_InactivoRechazado` — inactivo rechazado con la misma precedencia que la relectura
- [ ] `Buscar_SinTexto_PrimeraPaginaDe50` — D-01, D-07
- [ ] `Buscar_Armazon_PliegaElTexto` — validates AC-04
- [ ] `Modificar_Margen50A60_Graba1936` — validates AC-02
- [ ] `Modificar_Costo1210A2420_Graba3630` — validates AC-03
- [ ] `CambiarEstado_Desactivar_ConservaLosDatos` — validates AC-05
- [ ] `CambiarEstado_Reactivar_QuedaActivo` — validates AC-06
- [ ] `CambiarEstado_MismoEstado_NoEscribe` — D-09

**Completion criterion**
Las 19 pruebas pasan y `Optica.Application.Tests` completo queda en verde con el doble actualizado.

## Block 4 — Api: listado, modificación y cambio de estado

**Files**
- `src/Optica.Api/Controllers/ArticulosController.cs` (modified) — tres acciones nuevas; `Obtener` devuelve `activo` y `version`.
- `src/Optica.Api/Acceso/LimitesDelCuerpo.cs` (modified) — JSON obligatorio también para PUT (D-11).
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified) — `Conflicto(motivo, detalle)`: arma el 409 con `ProblemDetailsFactory`, `title` fijo y `Extensions["motivo"]`, como `Bloqueado` (D-19).
- `src/Optica.Api/Program.cs` (modified) — registra los tres casos de uso.
- `tests/Optica.Api.Tests/Catalogo/ArticulosApiTests.cs` (modified) — claves JSON con `activo` y `version`; casos nuevos.
- `tests/Optica.Api.Tests/Catalogo/CierrePorDefectoArticulosTests.cs` (modified) — constructor y traductores nuevos cerrados por defecto; los dos motivos del 409.
- `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs` (modified) — 401, 415 y 413 también para los PUT.
- `tests/Optica.Api.Tests/Acceso/SinDatosArcaTests.cs` (modified) — incluye `GET /api/articulos`.

**Logic**
Solo traduce HTTP y delega en los casos de uso de Block 3. Un `ModelState` inválido (parámetro ilegible o cuerpo
ilegible) responde 400 con un error fijo en el campo, sin el valor recibido. No registra la query string ni el cuerpo
(D-12). Los traductores son cerrados por defecto: un resultado no previsto lanza y el manejador responde 500 genérico.

**API contract**
- Method + path: `GET /api/articulos?texto={texto}&pagina={pagina}`
  - Request: `texto` (string, opcional), `pagina` (entero, opcional)
  - Response 200: `{ articulos: [{ codigo: long, codigoProveedor: string, descripcion: string, precioVenta: number, activo: bool }], pagina: int, tamanoPagina: 50, total: int }`
  - Error codes: 400 (texto o pagina inválidos), 401
- Method + path: `PUT /api/articulos/{codigo:long}`
  - Request: `{ codigoProveedor: string, descripcion: string, precioCosto: number, margenUtilidad: number, version: long? }`, JSON, hasta 1024 bytes
  - Response 200: el artículo grabado (`codigo`, `codigoProveedor`, `descripcion`, `precioCosto`, `margenUtilidad`, `precioVenta`, `advertencia`, `activo`, `version`), con costo y margen como el GET del detalle con sesión
  - Error codes: 400 (datos, duplicado, versión o cuerpo ilegible), 401, 404, 409 (ProblemDetails con `title` "No se pudo grabar el artículo." y extensión `motivo`: `inactivo` o `cambiado`, D-19), 413, 415
- Method + path: `PUT /api/articulos/{codigo:long}/estado`
  - Request: `{ activo: bool? }`, JSON, hasta 64 bytes
  - Response 200: el artículo con su estado, en la misma forma que el PUT de datos (incluye costo y margen, con sesión)
  - Error codes: 400 (`activo` faltante o cuerpo ilegible), 401, 404, 409 (`motivo`: `cambiado`, D-16), 413, 415
- Auth: sesión obligatoria en las tres (política por defecto, fuera de `RutasAnonimas`); cookie `SameSite=Strict`, sin CORS.

**Data model**
- Reuses `Articulo` from Block 2 — no schema change; constraints as declared there: NOT NULL, CHECK de `Activo` IN
  (0,1) y `Version` ≥ 0, índice UNIQUE de `CodigoProveedorClave`.

**Input validation**
- `texto` y `pagina`: las reglas de Block 3; `pagina` no entera → 400 en `pagina`.
- Cuerpos: números JSON estrictos (`JsonNumberHandling.Strict`), `version` `long?` (en la lista de campos de
  `CuerpoIlegible`, así un `version` ilegible cae en ese campo), `activo` `bool?`; tamaño y Content-Type por
  `RequestSizeLimit` y `LimitesDelCuerpo`.

**Error handling**
- Parámetro `pagina` ilegible — 400 con el error fijo en `pagina`.
- Cuerpo ilegible — 400 con un solo error y acción fija, como el alta.
- Artículo inexistente — 404 con ProblemDetails sin detalles.
- Artículo inactivo o cambiado — 409 con ProblemDetails, `title` fijo, `detail` con el texto de Block 3 y `motivo`.
- `version` faltante en el cuerpo — 400 en `version`, nunca grabación sin protección.
- Content-Type que no es JSON en un PUT — 415.
- Cuerpo demasiado grande — 413.
- Sin sesión — 401 antes que 415 y 413.

**Required tests**
- [ ] `Get_Listado_PaginaIlegible_400` — página inválida rechazada
- [ ] `Put_CuerpoIlegible_400` — cuerpo ilegible rechazado
- [ ] `Put_Inexistente_404` — artículo inexistente rechazado
- [ ] `Put_Inactivo_409` — conflicto por inactivo rechazado
- [ ] `Put_Cambiado_409` — conflicto por versión rechazado, con `motivo` `cambiado`
- [ ] `Put_VersionFaltante_400` — versión faltante rechazada en `version`
- [ ] `PutEstado_Cambiado_409` — conflicto del cambio de estado rechazado
- [ ] `Put_SinJson_415` (en `ProteccionTests`, para los dos PUT) — Content-Type inválido rechazado
- [ ] `Put_DemasiadoGrande_413` (en `ProteccionTests`, 1024 y 64 bytes) — cuerpo inválido rechazado
- [ ] `RutasNuevas_SinSesion_401` (recorrido de `ProteccionTests`) — no autorizado
- [ ] `Get_Articulo_MuestraPrecioVentaYVersion` — validates AC-01
- [ ] `Put_Margen50A60_200Con1936` — validates AC-02
- [ ] `Put_Costo1210A2420_200Con3630` — validates AC-03
- [ ] `Get_Listado_Armazon_SinCostoNiMargen` — validates AC-04, D-12
- [ ] `PutEstado_Desactivar_ListadoInactivo` — validates AC-05
- [ ] `PutEstado_Reactivar_ListadoActivo` — validates AC-06
- [ ] `Traductores_ResultadoNoPrevisto_Lanzan` (`CierrePorDefectoArticulosTests`) — error no previsto rechazado

**Completion criterion**
Las 17 pruebas pasan, `RutasAnonimas` sigue con cinco rutas y `Optica.Api.Tests` queda en verde.

## Block 5 — Web: listado y edición de artículos

**Files**
- `web/src/paginas/Articulos.tsx` (new) y `Articulos.test.tsx` (new) — búsqueda, tabla, páginas, desactivar/reactivar.
- `web/src/paginas/EditarArticulo.tsx` (new) y `EditarArticulo.test.tsx` (new) — edición; solo lectura si inactivo.
- `web/src/api/cliente.ts` (modified) — `buscarArticulos`, `modificarArticulo`, `cambiarEstadoArticulo`; `metodo` admite `PUT`; `hacerPedido` pone `Content-Type: application/json` en POST y en PUT (si no, 415 por D-11); `traducirError` lee el JSON de un 409 y la variante `conflicto` de `Fallo` lleva el `motivo` (`inactivo`, `cambiado`; desconocido → respuesta ilegible, D-19); `leerArticulo` exige `activo` y `version`.
- `web/src/api/cliente.test.ts` (modified) — fixtures con `activo` y `version`; casos nuevos.
- `web/src/App.tsx` y `App.test.tsx` (modified) — rutas `/articulos` y `/articulos/:codigo`.
- `web/src/paginas/Inicio.tsx` y `Inicio.test.tsx` (modified) — enlace "Artículos".
- `web/src/paginas/NuevoArticulo.test.tsx` (modified) — fixtures con `activo` y `version`.

**Logic**
- `/articulos`: campo de búsqueda (Buscar con Enter o botón), tabla con Código, Código en el proveedor, Descripción,
  Precio de Venta (`mostrarImporte`) y Estado ("Activo"/"Inactivo"), Anterior/Siguiente según `total`, enlace al
  artículo y botón Desactivar o Reactivar por fila que refresca la página actual.
- `/articulos/:codigo`: carga el artículo; Precio de Venta en un campo de solo lectura (AC-01); Grabar envía los
  cuatro datos y la `version` leída y muestra el precio nuevo; inactivo: todos los campos de solo lectura, aviso
  "Este artículo está inactivo. Reactivalo para editarlo." y botón Reactivar (D-02).
- Mismas reglas de pantalla que el alta (`catalogo/reglas.ts`) y los mismos errores por campo.

**Data model**
- Reuses `Articulo` from Block 2 — no schema change; constraints as declared there: NOT NULL, CHECK de largos,
  margen, `Activo` IN (0,1) y `Version` ≥ 0, índice UNIQUE de `CodigoProveedorClave`.

**Input validation**
- Búsqueda: hasta 200 caracteres tras el recorte de D-17.
- Formulario: las reglas de `catalogo/reglas.ts` (largos, 2 decimales, margen de 0 a 1.000).
- `:codigo` de la ruta: entero positivo; si no, mensaje "No se encontró el artículo."

**Error handling**
- Código de ruta inválido o 404 — mensaje "No se encontró el artículo." y enlace al listado.
- 400 con errores — cada mensaje junto a su campo.
- 409 — según `motivo`, el texto propio de la pantalla arriba del formulario ("Este artículo está inactivo.
  Reactivalo para editarlo." o "El artículo cambió mientras lo editabas. Volvé a abrirlo.") y botón para volver a
  abrirlo; un `motivo` desconocido se trata como respuesta ilegible (D-19). En el listado, un 409 del cambio de
  estado muestra "El artículo cambió. Volvé a cargar el listado." y recarga la página.
- Respuesta ilegible o error de red — mensaje general, sin importes falsos (D-19 de FEAT-002a).
- 401 — vuelve al ingreso, como el resto de la SPA.

**Required tests**
- [ ] `EditarArticulo_CodigoInvalido_NoEncontrado` — código inválido rechazado
- [ ] `EditarArticulo_ErroresPorCampo_400` — datos inválidos rechazados junto al campo
- [ ] `EditarArticulo_Conflicto409_MuestraMensaje` — conflicto rechazado con mensaje
- [ ] `Articulos_RespuestaIlegible_MensajeGeneral` — error de respuesta ilegible
- [ ] `modificarArticulo_409ConMotivoDesconocido_RespuestaIlegible` (`cliente.test.ts`) — motivo inválido rechazado
- [ ] `modificarArticulo_EnviaJsonConPut` (`cliente.test.ts`) — sin el Content-Type sería un error 415
- [ ] `Articulos_SinSesion401_VuelveAlIngreso` — no autorizado
- [ ] `EditarArticulo_PrecioVentaSoloLectura` — validates AC-01
- [ ] `EditarArticulo_GrabarMargen_MuestraPrecioNuevo` — validates AC-02
- [ ] `EditarArticulo_GrabarCosto_MuestraPrecioNuevo` — validates AC-03
- [ ] `Articulos_Buscar_MuestraCoincidencias` — validates AC-04
- [ ] `Articulos_Desactivar_MuestraInactivo` — validates AC-05
- [ ] `Articulos_Reactivar_MuestraActivo` — validates AC-06
- [ ] `EditarArticulo_Inactivo_SoloLecturaConReactivar` — D-02
- [ ] `Articulos_Paginas_AnteriorYSiguiente` — D-01

**Completion criterion**
Las 15 pruebas pasan, `npm run test:coverage` y `npm run build` quedan en verde con cobertura ≥ 80 %.

## Block 6 — E2E: AC-01 a AC-06 en Chrome y Edge

**Files**
- `e2e/tests/articulos-edicion.spec.ts` (new) — los seis AC sobre la Api y la SPA reales.
- `e2e/README.md` (modified) — proyectos y puertos nuevos.

**Logic**
Cada prueba crea sus propios artículos por la Api con sesión sobre la base temporal del proyecto, configura alícuota
21 y múltiplo 0,01, y recorre la pantalla como la operadora. Los proyectos `chrome-articulos-edicion` y
`msedge-articulos-edicion` se derivan solos del archivo.

**Data model**
- Reuses `Articulo` from Block 2 — no schema change; constraints as declared there: NOT NULL, CHECK, índice UNIQUE
  de la clave, `Activo` y `Version`.

**Input validation**
- Las pruebas escriben solo datos válidos salvo la del caso inválido; los textos los fija cada prueba.

**Error handling**
- Edición con un código ya usado — la pantalla muestra el error junto al código.

**Required tests**
- [ ] `edicion con codigo duplicado muestra el error rechazado junto al campo` — duplicado rechazado
- [ ] `al abrir un articulo el precio de venta es de solo lectura` — validates AC-01
- [ ] `cambiar el margen de 50 a 60 graba 1.936,00` — validates AC-02
- [ ] `cambiar el costo de 1.210 a 2.420 graba 3.630,00` — validates AC-03
- [ ] `buscar armazon lista los que contienen Armazón o armazon y ningún otro` — validates AC-04
- [ ] `desactivar muestra inactivo y conserva los datos` — validates AC-05
- [ ] `reactivar muestra activo` — validates AC-06

**Completion criterion**
Las 7 pruebas pasan en `chrome-articulos-edicion` y `msedge-articulos-edicion` con `npm run test:harness`.

## Final verification
Las suites completas (`dotnet test Optica.sln`, `npm run test:coverage` en `web/`, `npm run test:unidad` y
`npm run test:harness` en `e2e/`) pasan; AC-01 a AC-06 tienen prueba de punta a punta; `HasPendingModelChanges` es
falso; ninguna operación del catálogo escribe fuera de la tabla `Articulo` (D-13); `plegar` no aparece en ningún
objeto del esquema (ADR-011).
