# Spec FEAT-003a: Alta del presupuesto con cliente y líneas del catálogo

| Field | Value |
|-------|-------|
| Ticket | FEAT-003a |
| PRD | docs/ddw/prd/prd-FEAT-003a.md |
| Tier | FEATURE |
| Date | 2026-11-06 |
| Spec loops | 1 |
| Loops since last human decision | 0 |

## Summary
Un módulo nuevo, Presupuestos, que depende de Catálogo y nunca al revés (D-05). El dominio suma el cliente del
presupuesto con sus reglas (Apellido, Nombre y DNI obligatorios; DNI de 7 u 8 dígitos guardado sin puntos; Email con
formato `usuario@dominio.extensión`; largos de D-08), la línea tomada de un artículo con Cantidad 1 y Descuento 0
(D-03), el presupuesto en Borrador y la numeración último + 1 desde 1 (D-02, D-07). La migración `Presupuestos` crea
las tablas `Presupuesto` y `LineaPresupuesto` con importes en centésimos (ADR-008), CHECK y claves foráneas, sin
reconstruir tablas existentes (ADR-012). El alta lee los artículos, el último número y la fecha local del día (D-06)
dentro de la transacción de escritura de ADR-013. La Api expone `POST /api/presupuestos` y
`GET /api/presupuestos/{numero}` con sesión, y `GET /api/articulos` suma `soloActivos` para buscar solo artículos
activos (D-15). La SPA suma el alta `/presupuestos/nuevo` y el presupuesto grabado `/presupuestos/{numero}`.

**Datos personales (Ley 25.326).** DNI, Domicilio, Email y Nro. de Teléfono, y también Apellido y Nombre, no llegan
al log ni a `ToString`; toda operación exige sesión; los mensajes de error son textos fijos que no repiten el valor
ingresado; ningún dato personal viaja en la URL ni se guarda en el almacenamiento del navegador (D-17, D-23).

**Textos.** El PRD fija dos textos: "Ingresá el DNI del cliente" (AC-02) y "El precio unitario no puede
ser negativo" (AC-16), que se usan tal cual, sin punto final. Los demás textos que la operadora ve (AC-03, AC-06,
AC-07, AC-11, el mensaje de confirmación de AC-01 y AC-12, la línea con un artículo que ya no está disponible y el
presupuesto no encontrado) no los fija el PRD: la spec los nombra como constantes con los textos que el usuario
confirmó (D-24), y las pruebas comparan contra esas constantes. Que no se pueda modificar la fecha
(FEAT-003 AC-11) y los triggers de inmutabilidad (`CierrePresupuestos`) son de FEAT-003c.
Decisiones: `docs/ddw/specs/decisions-FEAT-003a.md` (D-01 a D-27).

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2, Block 3, Block 4, Block 5, Block 6 |
| FR-02 | Block 1, Block 3, Block 4, Block 5, Block 6 |
| FR-03 | Block 1, Block 2, Block 3, Block 4, Block 5, Block 6 |
| FR-04 | Block 1, Block 2, Block 3, Block 4, Block 5, Block 6 |
| FR-05 | Block 1, Block 3, Block 4, Block 5, Block 6 |
| FR-06 | Block 1, Block 2, Block 3, Block 4, Block 6 |
| FR-07 | Block 1, Block 3, Block 4, Block 5, Block 6 |
| FR-08 | Block 1, Block 2, Block 3, Block 4, Block 5, Block 6 |
| FR-09 | Block 2, Block 3, Block 4, Block 5, Block 6 |
| FR-10 | Block 1, Block 3, Block 4, Block 5, Block 6 |
| FR-11 | Block 3, Block 5, Block 6 |
| FR-12 | Block 1, Block 3, Block 4, Block 5, Block 6 |
| FR-13 | Block 1, Block 2, Block 4, Block 5, Block 6 |
| FR-14 | Block 1, Block 2, Block 3, Block 4, Block 6 |
| FR-15 | Block 1, Block 2, Block 3, Block 4, Block 5, Block 6 |
| NFR-01 | Strategy: Block 6 corre AC-01 a AC-19 en los proyectos `chrome-presupuestos-alta` y `msedge-presupuestos-alta` (canal estable instalado); la versión anterior de cada navegador se verifica a mano, como en FEAT-001 y FEAT-002 |
| NFR-02 | Strategy: importes en centésimos enteros (ADR-008) con los conversores existentes de `OpticaDbContext`; el Precio Unitario se copia del precio de venta ya redondeado (ADR-010) sin aritmética nueva; la Api devuelve los importes con 2 decimales y la pantalla los formatea con `mostrarImporte` de FEAT-002a ("$1.850,00") |

## Dependencies between blocks
Block 1 → Block 2 → Block 3 → Block 4 → Block 5 → Block 6. Cada bloque usa los tipos del anterior. Block 2 cambia la
firma de `IArticuloRepository.BuscarAsync` y Block 3 la usa desde `BuscarArticulos`.

## Block 1 — Dominio: cliente, línea, presupuesto y numeración

**Files**
- `src/Optica.Domain/Presupuestos/PoliticaCliente.cs` (new) — `InfraccionCliente`, `PoliticaCliente.Evaluar`, `PoliticaCliente.NormalizarDni` y los largos de D-08.
- `src/Optica.Domain/Presupuestos/ClientePresupuesto.cs` (new) — los seis datos del cliente, validados y normalizados.
- `src/Optica.Domain/Presupuestos/LineaPresupuesto.cs` (new) — la línea, `LineaPresupuesto.DesdeArticulo` y `ResultadoLinea`.
- `src/Optica.Domain/Presupuestos/Presupuesto.cs` (new) — `Presupuesto`, `EstadoPresupuesto` y `NumeracionPresupuesto`.
- `src/Optica.Domain/Presupuestos/IPresupuestoRepository.cs` (new)
- `tests/Optica.Domain.Tests/Presupuestos/PoliticaClienteTests.cs`, `LineaPresupuestoTests.cs` y `PresupuestoTests.cs` (new)

**Logic**
- `PoliticaCliente.Evaluar(apellido, nombre, dni, domicilio, email, telefono)` → lista de `InfraccionCliente`, una
  por campo como máximo, en ese orden; por campo gana la primera regla incumplida (faltante, UTF-16 inválido, largo,
  formato), como `PoliticaArticulo`. Recorta con `NormalizacionCodigo.Recortar` (D-17 de FEAT-002a) y valida UTF-16
  con `NormalizacionCodigo.EsUtf16Valido` (D-25). Apellido, Nombre y DNI obligatorios (FR-02); Domicilio, Email y Teléfono
  opcionales: vacíos o solo espacios valen como ausentes y se guardan null (FR-03, D-11). Nunca lanza.
- `PoliticaCliente.NormalizarDni(texto)`: recorta, quita todos los "." y devuelve los dígitos si quedan 7 u 8 dígitos
  ASCII; si no, null (FR-04, D-09). "23.456.789" → "23456789".
- Email válido (FR-05, D-10): parte local sin espacios ni "@", una "@" y un dominio con al menos un punto, sin espacios
  ni partes vacías, con una extensión final de al menos 2 letras ("ana@mail.c" inválido); hasta 254 caracteres.
- `new ClientePresupuesto(apellido, nombre, dni, domicilio, email, telefono)`: evalúa la política y lanza
  `ArgumentException` con el nombre del campo y sin el valor si algo incumple; guarda los textos recortados, el DNI
  normalizado y null en los opcionales vacíos. No sobrescribe `ToString` (D-17).
- `LineaPresupuesto.DesdeArticulo(Articulo articulo)` → `ResultadoLinea`: artículo inactivo → `Inactivo`; precio de
  venta negativo → `PrecioNegativo` (FR-12); si no, `Valida` con una línea que copia `Codigo`, `Descripcion` y
  `PrecioVenta` como Precio Unitario, sin recalcular (FR-10), con Cantidad 1, Porcentaje de Descuento 0 y Precio con
  Descuento y Precio Final iguales al Precio Unitario (FR-13, FR-14, D-03). Precio 0 se admite (riesgo del PRD).
- `new LineaPresupuesto(codigoArticulo, descripcion, precioUnitario, cantidad, porcentajeDescuento,
  precioConDescuento, precioFinal)` reconstruye una línea guardada y verifica sus invariantes: código > 0, descripción
  de 1 a 200 caracteres, importes ≥ 0 con hasta 2 decimales y dentro de `ReglasImporte.ImporteMaximo`, Cantidad ≥ 1,
  Descuento de 0 a 100 con hasta 2 decimales.
- `NumeracionPresupuesto.Siguiente(long? ultimo)`: null → 1; si no, `ultimo + 1` (FR-06, D-02, D-07).
- `Presupuesto.Nuevo(numero, fecha, cliente, lineas)`: estado `Borrador` (FR-08), número ≥ 1, fecha `DateOnly`
  (FR-15), al menos una línea (FR-07). El constructor de reconstrucción recibe además el estado.
- `IPresupuestoRepository`: `ObtenerUltimoNumeroAsync()` → `long?`, `Agregar(Presupuesto)`,
  `ObtenerAsync(long numero)` → `Presupuesto?`. `Agregar` solo registra: confirma la unidad de trabajo.

**Data model**
- Entidad `Presupuesto` del dominio, sin tabla en este bloque: `Numero` long NOT NULL (≥ 1), `Fecha` DateOnly NOT
  NULL, `Estado` NOT NULL (`Borrador` o `Final`), `Cliente` NOT NULL, `Lineas` NOT NULL con al menos una.
- `ClientePresupuesto`: Apellido y Nombre NOT NULL (1 a 100), DNI NOT NULL (7 u 8 dígitos), Domicilio nullable (hasta
  200), Email nullable (hasta 254, con formato), Teléfono nullable (hasta 30).
- `LineaPresupuesto`: código de artículo NOT NULL (> 0), descripción NOT NULL (1 a 200), cuatro importes NOT NULL
  (≥ 0, centésimos por ADR-008), Cantidad NOT NULL (≥ 1), Descuento NOT NULL (0 a 100). Block 2 los persiste con CHECK.

**Input validation**
- Apellido y Nombre: obligatorios, hasta 100 caracteres tras el recorte, UTF-16 válido.
- DNI: obligatorio; 7 u 8 dígitos después de quitar los puntos (D-09).
- Domicilio hasta 200, Email hasta 254 con el formato de D-10, Teléfono hasta 30 sin formato; UTF-16 válido.
- Línea: el artículo tiene código, está activo y su precio de venta es ≥ 0.

**Error handling**
- Apellido o Nombre faltante, largo o con UTF-16 inválido — infracción del campo (`ApellidoFaltante`, `NombreFaltante`, `…DemasiadoLargo`, `…ConCaracteresInvalidos`).
- DNI faltante — `DniFaltante`.
- DNI con otra cantidad de dígitos o con otro carácter — `DniInvalido`.
- Email sin el formato o demasiado largo — `EmailInvalido` o `EmailDemasiadoLargo`.
- Domicilio o Teléfono demasiado largo o con UTF-16 inválido — infracción del campo.
- Artículo con precio de venta negativo — `ResultadoLinea.PrecioNegativo`, sin línea.
- Artículo inactivo — `ResultadoLinea.Inactivo`, sin línea; un artículo sin código lanza `ArgumentException`.
- Presupuesto sin líneas, número menor que 1 o línea con invariantes rotas — `ArgumentException` con el nombre del campo, sin valores (la aplicación valida antes).
- Siguiente número fuera de rango (último = `long.MaxValue`) — `InvalidOperationException`, nunca desborde.

**Required tests**
- [ ] `Evaluar_SinApellidoOSinNombre_InfraccionFaltante` — apellido o nombre faltante rechazado en su campo; validates AC-03
- [ ] `Evaluar_SinDni_DniFaltante` — DNI faltante rechazado; validates AC-02
- [ ] `Evaluar_Dni123456_123456789_23A56789_DniInvalido` — DNI inválido rechazado; validates AC-06
- [ ] `Evaluar_EmailAnaArrobaYAnaArrobaMail_EmailInvalido` — email inválido rechazado; validates AC-07
- [ ] `Evaluar_EmailExtensionDeUnaLetra_EmailInvalido` — "ana@mail.c" inválido rechazado (D-10); validates AC-07
- [ ] `Evaluar_EmailAnaArrobaMailComAr_Valido` — "ana@mail.com.ar" válido (D-10); validates AC-08
- [ ] `Evaluar_DomicilioYTelefonoLargos_Rechazados` — largo excedido rechazado (D-08)
- [ ] `Evaluar_Utf16Invalido_Rechazado` — texto inválido rechazado en cada campo, sin lanzar
- [ ] `Evaluar_ApellidoConEspaciosAlrededor_SeRecorta` — "  Pérez  " se graba "Pérez" y "   " cuenta como apellido faltante rechazado (D-25)
- [ ] `DesdeArticulo_PrecioMenos150_Rechazado` — precio negativo rechazado, sin línea; validates AC-16
- [ ] `DesdeArticulo_Inactivo_Rechazado` — artículo inactivo rechazado (FR-09, D-14)
- [ ] `Nuevo_SinLineas_Rechazado` — presupuesto sin líneas rechazado con `ArgumentException`; validates AC-11
- [ ] `ConstructorLinea_InvariantesRotas_Error` — línea inválida rechazada, mensaje sin valores
- [ ] `Siguiente_UltimoMaximo_Error` — número fuera de rango: error, no desborde
- [ ] `Evaluar_SeisDatosValidos_SinInfracciones` — validates AC-01
- [ ] `Evaluar_SinDomicilioEmailNiTelefono_SinInfracciones` — validates AC-04
- [ ] `NormalizarDni_23Punto456Punto789_23456789` — validates AC-05
- [ ] `Evaluar_EmailAnaArrobaMailCom_Valido` — validates AC-08
- [ ] `Siguiente_Ultimo154_155` — validates AC-09
- [ ] `Siguiente_SinPresupuestos_1` — validates AC-10
- [ ] `Nuevo_QuedaEnBorrador` — validates AC-12
- [ ] `DesdeArticulo_1850_CopiaCodigoDescripcionYPrecio` — validates AC-15
- [ ] `DesdeArticulo_1850_Cantidad1Descuento0YPreciosIguales` — validates AC-18 y AC-17 (las siete columnas de la línea)
- [ ] `Nuevo_Fecha10_03_2026_LaConserva` — validates AC-19
- [ ] `DesdeArticulo_PrecioCero_SeAdmite` — riesgo del PRD (precio 0)
- [ ] `ToString_ClienteYPresupuesto_SinDatosPersonales` — D-17: `ToString` no contiene apellido, nombre, DNI, domicilio, email ni teléfono

**Completion criterion**
Las 26 pruebas pasan, `Optica.Domain.Tests` queda en verde y ningún tipo de `Optica.Domain.Catalogo` referencia a
`Optica.Domain.Presupuestos`.

## Block 2 — Infraestructura: tablas, migración `Presupuestos`, repositorio y filtro de activos

**Files**
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (modified) — `PresupuestoFila`, `LineaPresupuestoFila`, su mapeo con CHECK, claves foráneas y centésimos; `DbSet` de las dos.
- `src/Optica.Infrastructure/Persistencia/Migraciones/{timestamp}_Presupuestos.cs` y `.Designer.cs` (new), `OpticaDbContextModelSnapshot.cs` (modified).
- `src/Optica.Infrastructure/Persistencia/PresupuestoRepository.cs` (new)
- `src/Optica.Infrastructure/ConfiguracionInfraestructura.cs` (modified) — registra `IPresupuestoRepository`.
- `src/Optica.Domain/Catalogo/IArticuloRepository.cs` y `src/Optica.Infrastructure/Persistencia/ArticuloRepository.cs` (modified) — `BuscarAsync(textoPlegado, pagina, tamano, soloActivos = false, cancelacion = default)` (D-15): el parámetro nuevo va después de `tamano` y antes del `CancellationToken`, así las llamadas posicionales existentes siguen compilando; `src/Optica.Application/Catalogo/BuscarArticulos.cs` (modified) pasa `soloActivos: false` hasta Block 3; `tests/Optica.Application.Tests/Catalogo/Dobles.cs` (modified) y `tests/Optica.Api.Tests/Catalogo/ArticulosApiTests.cs` (modified) — el doble privado `ArticulosQueFallan` toma la firma nueva en este bloque, para que `dotnet build Optica.sln` compile entre Block 2 y Block 4.
- `tests/Optica.Infrastructure.Tests/Persistencia/PresupuestoRepositoryTests.cs` (new), `ArticuloRepositoryTests.cs` (modified: pruebas de `soloActivos` y la aserción de la última migración, hoy `_CatalogoActivo`, pasa a `_Presupuestos`), `tests/Optica.Infrastructure.Tests/Acceso/AccesoPersistenciaTests.cs` (modified) — lista de migraciones con `Presupuestos`.

**Logic**
- `ObtenerUltimoNumeroAsync`: `MAX(Numero)` como `long?` (null sin presupuestos); se llama dentro de la operación del
  alta (D-07).
- `Agregar(presupuesto)`: rechaza un presupuesto sin líneas o con número < 1; agrega la fila y sus líneas con `Orden`
  1..n en el orden recibido. Los importes pasan por el conversor de centésimos existente; la fecha, por el mapeo de
  `DateOnly` de EF Core a TEXT `yyyy-MM-dd`; el estado, como `borrador` o `final` (D-19).
- `ObtenerAsync(numero)`: sin seguimiento, con las líneas ordenadas por `Orden`; arma la entidad con sus
  constructores, y una fila que no los cumple es estado corrupto.
- `ArticuloRepository.BuscarAsync` con `soloActivos` verdadero suma `Activo = 1` al filtro, al conteo y a la página;
  el resto (plegado, `instr`, orden `orden_articulo`, desplazamiento en `long`) no cambia (D-15, ADR-011).
- Sin `EnableSensitiveDataLogging`, como hoy: EF registra el SQL con los nombres de los parámetros y sin sus valores, así ningún dato del cliente llega al log (D-17). No se cambia el nivel de log de `appsettings.json`.

**Data model**
- Tabla nueva `Presupuesto`: `Numero` INTEGER PRIMARY KEY (sin AUTOINCREMENT, `ValueGeneratedNever`, CHECK
  `"Numero" >= 1`); `Fecha` TEXT NOT NULL (CHECK `"Fecha" GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]'`);
  `Estado` TEXT NOT NULL (CHECK `"Estado" IN ('borrador', 'final')`, `CK_Presupuesto_Estado`); `Apellido` y `Nombre`
  TEXT NOT NULL (CHECK `length` BETWEEN 1 AND 100); `Dni` TEXT NOT NULL (CHECK `length("Dni") BETWEEN 7 AND 8 AND
  "Dni" NOT GLOB '*[^0-9]*'`, `CK_Presupuesto_Dni`); `Domicilio` TEXT NULL (CHECK `length` ≤ 200); `Email` TEXT NULL
  (CHECK `length` ≤ 254); `Telefono` TEXT NULL (CHECK `length` ≤ 30). Cada CHECK lleva nombre `CK_Presupuesto_<Columna>`.
- Tabla nueva `LineaPresupuesto`: clave primaria compuesta (`PresupuestoNumero`, `Orden`); `PresupuestoNumero`
  INTEGER NOT NULL, FOREIGN KEY → `Presupuesto(Numero)` ON DELETE RESTRICT; `Orden` INTEGER NOT NULL (CHECK ≥ 1);
  `CodigoArticulo` INTEGER NOT NULL, FOREIGN KEY → `Articulo(Id)` ON DELETE RESTRICT, con índice
  `IX_LineaPresupuesto_CodigoArticulo`; `Descripcion` TEXT NOT NULL (CHECK `length` BETWEEN 1 AND 200);
  `PrecioUnitarioCentavos`, `PrecioConDescuentoCentavos` y `PrecioFinalCentavos` INTEGER NOT NULL (CHECK ≥ 0);
  `Cantidad` INTEGER NOT NULL (CHECK ≥ 1); `DescuentoCentesimos` INTEGER NOT NULL (CHECK BETWEEN 0 AND 10000). Cada
  CHECK lleva nombre `CK_LineaPresupuesto_<Columna>`.
- Sin DEFAULT: el repositorio asigna todas las columnas. Las claves foráneas valen porque `FabricaConexion` ya activa
  `PRAGMA foreign_keys = ON` en cada conexión.
- Tabla `Articulo` sin cambios: la migración solo crea tablas (D-18, ADR-012); `sqlite_sequence` de `Articulo` queda
  intacta. Sin triggers (FEAT-003c).
- Rollback: el `Down` de `Presupuestos` borra `LineaPresupuesto` y después `Presupuesto`; la prueba de revertir y volver
  a aplicar de `AccesoPersistenciaTests` lo cubre. Revertir pierde los presupuestos grabados.

**Input validation**
- `Agregar`: presupuesto no nulo, con número ≥ 1 y al menos una línea; los demás datos ya los validó el dominio.
- `ObtenerAsync`: número de la ruta, cualquier `long` (inexistente → null).
- `BuscarAsync`: los parámetros de FEAT-002b, más `soloActivos` booleano.

**Error handling**
- Fila con estado, DNI, fecha o importes que eluden las reglas — `InvalidOperationException` de estado corrupto, sin valores.
- `Agregar` con un presupuesto sin número o sin líneas — `ArgumentException` sin valores.
- Número repetido al confirmar — `ConflictoAlConfirmarException` con motivo `Unicidad` (clave primaria).
- Línea con un código de artículo inexistente — la clave foránea rechaza la grabación y la falla se propaga (no es un conflicto de D-18 de FEAT-002b).
- Valor fuera de un CHECK — la falla se propaga y no se graba nada.

**Required tests**
- [ ] `ObtenerAsync_FilaCorrupta_EsEstadoCorrupto` — fila con DNI o estado inválido rechazada como corrupta, error sin valores
- [ ] `Agregar_SinNumero_Rechazado` — presupuesto inválido rechazado
- [ ] `Confirmar_NumeroRepetido_ConflictoDeUnicidad` — conflicto de clave primaria
- [ ] `Confirmar_ArticuloInexistente_FallaPorClaveForanea` — error de clave foránea, nada grabado
- [ ] `Esquema_ChecksRechazanValoresInvalidos` — inserción cruda con DNI de 6 dígitos, estado desconocido, cantidad 0 o importe negativo rechazada por CHECK
- [ ] `ObtenerUltimoNumero_Con154_Devuelve154` — validates AC-09
- [ ] `ObtenerUltimoNumero_SinPresupuestos_Null` — validates AC-10
- [ ] `Agregar_SeisDatosYUnaLinea_SeLeeIgual` — validates AC-01 y AC-17
- [ ] `Agregar_SinOpcionales_GuardaNull` — validates AC-04
- [ ] `Agregar_Dni23456789_GuardaSinPuntos` — validates AC-05
- [ ] `Agregar_Borrador_GuardaBorrador` — validates AC-12
- [ ] `Agregar_Linea1850_GuardaCentavosCantidad1YDescuento0` — validates AC-18
- [ ] `Agregar_Fecha10_03_2026_GuardaTextoYLeeIgual` — validates AC-19
- [ ] `BuscarAsync_SoloActivosArmazon_ExcluyeInactivos` — validates AC-13
- [ ] `BuscarAsync_SoloActivosPorCodigo_ListaElArticulo` — validates AC-14
- [ ] `Migracion_Presupuestos_NoTocaArticuloNiSuSecuencia` — ADR-012
- [ ] `Esquema_Presupuesto_ColumnasNotNullChecksYForeignKeys` — esquema exacto de las dos tablas

**Completion criterion**
Las 17 pruebas pasan, `HasPendingModelChanges` es falso, la lista de migraciones termina en `Presupuestos` y
`Optica.Infrastructure.Tests` y `Optica.Application.Tests` quedan en verde.

## Block 3 — Aplicación: alta, consulta y búsqueda de artículos activos

**Files**
- `src/Optica.Application/Presupuestos/ValidacionPresupuesto.cs` (new) — campos (D-20), textos y traducción de `InfraccionCliente`.
- `src/Optica.Application/Presupuestos/AltaPresupuesto.cs` (new) — `DatosCliente`, `DatosPresupuesto`, `ResultadoAltaPresupuesto`, `PresupuestoGrabado`, `ClienteGrabado`, `LineaGrabada`.
- `src/Optica.Application/Presupuestos/ObtenerPresupuesto.cs` (new)
- `src/Optica.Application/Catalogo/BuscarArticulos.cs` (modified) — parámetro `soloActivos` (D-15).
- `tests/Optica.Application.Tests/Presupuestos/Dobles.cs`, `AltaPresupuestoTests.cs` y `ObtenerPresupuestoTests.cs` (new); `tests/Optica.Application.Tests/Catalogo/BuscarArticulosTests.cs` y `tests/Optica.Application.Tests/Arquitectura/DependenciasDeModulosTests.cs` (modified)

**Logic**
- `AltaPresupuesto(IPresupuestoRepository, IArticuloRepository, IUnidadDeTrabajo, TimeProvider)`.
  `EjecutarAsync(DatosPresupuesto datos)`:
  1. Valida el cliente con `PoliticaCliente` y las líneas (al menos una, FR-07; hasta 100 y cada código de artículo
     presente y > 0, D-16) y devuelve `Invalido` con todos los errores, sin leer la base.
  2. Dentro de `EjecutarEnTransaccionAsync` (ADR-013): lee cada artículo con `ObtenerAsync`; inexistente o `Inactivo`
     → error en `lineas[i]`; `PrecioNegativo` → error en `lineas[i]` con el texto de AC-16 (D-14); con algún error
     devuelve `Invalido` sin registrar nada. Si no, lee el último número, calcula `NumeracionPresupuesto.Siguiente`,
     toma la fecha `DateOnly.FromDateTime(reloj.GetLocalNow().DateTime)` (D-06), arma `Presupuesto.Nuevo`, lo agrega y
     confirma. `ConflictoAlConfirmarException` no se atiende: la operación revierte y se propaga (D-07).
  3. Después de la operación relee el presupuesto por número y devuelve `Grabado` con `PresupuestoGrabado`.
- `ObtenerPresupuesto.EjecutarAsync(long numero)` → `PresupuestoGrabado?`.
- `PresupuestoGrabado(Numero, Fecha, Estado, Cliente, Lineas)`; `LineaGrabada` con las siete columnas de FR-13.
- `BuscarArticulos.EjecutarAsync(texto, pagina, soloActivos = false, cancelacion = default)` (el nuevo parámetro va antes del `CancellationToken`; los llamadores existentes pasan `cancelacion` por nombre) pasa el filtro al repositorio; el resto sin
  cambios.
- Ningún caso de uso registra en el log datos del cliente (D-17). `DatosCliente`, `DatosPresupuesto`,
  `PresupuestoGrabado` y `ClienteGrabado` no imprimen sus miembros (`PrintMembers` devuelve falso).

**Data model**
- Reuses `Presupuesto` and `LineaPresupuesto` from Block 2 — no schema change; constraints as declared there: PK
  `Numero`, NOT NULL, CHECK de DNI, estado, largos e importes, FOREIGN KEY de la línea al presupuesto y al artículo.
- Reuses `Articulo` from FEAT-002b — no schema change; `Activo` IN (0, 1).

**Input validation**
- Los seis datos del cliente: las reglas de `PoliticaCliente` (Block 1).
- `lineas`: al menos una y hasta 100; cada `codigoArticulo` presente, entero > 0 y hasta 2^53 − 1.
- `numero` de la consulta: cualquier `long`.
- `soloActivos`: booleano opcional (ausente = falso).

**Error handling**
- Datos del cliente inválidos — `Invalido` con un error por campo, sin leer la base. Textos: DNI faltante "Ingresá el DNI del cliente" (AC-02); los demás, constantes de `ValidacionPresupuesto` (D-24: "Ingresá el apellido del cliente", "Ingresá el nombre del cliente", "El DNI debe tener 7 u 8 dígitos", "El email debe tener el formato usuario@dominio.extensión"); largo y UTF-16 inválido, constantes propias de `ValidacionPresupuesto` con el mismo texto que el catálogo ("Usá hasta N caracteres.", "El texto tiene caracteres no válidos. Escribilo de nuevo."), sin referenciar `Optica.Application.Catalogo` (D-08). Ninguno repite el valor.
- Sin líneas — `Invalido` en `lineas` (D-24: "Agregá al menos una línea al presupuesto"), sin leer la base.
- Más de 100 líneas o código de artículo faltante o no positivo — `Invalido` en `lineas` o `lineas[i]`.
- Artículo inexistente o inactivo — `Invalido` en `lineas[i]` (D-24: "Ese artículo ya no está disponible. Quitá la línea y buscalo de nuevo"), nada grabado.
- Artículo con precio negativo — `Invalido` en `lineas[i]` con "El precio unitario no puede ser negativo", nada grabado.
- Falla o conflicto al confirmar — la operación revierte y la excepción se propaga (500 genérico en la Api).
- Presupuesto inexistente en la consulta — null.

**Required tests**
- [ ] `Alta_SinApellidoSinNombreSinDni_TodosLosErroresSinLeer` — datos faltantes rechazados con el texto de AC-02; validates AC-02 y AC-03
- [ ] `Alta_Dni123456_ErrorEnDni` — DNI inválido rechazado; validates AC-06
- [ ] `Alta_EmailAnaArroba_ErrorEnEmail` — email inválido rechazado; validates AC-07
- [ ] `Alta_SinLineas_ErrorEnLineas` — presupuesto sin líneas rechazado; validates AC-11
- [ ] `Alta_101LineasOCodigoFaltante_Rechazado` — líneas inválidas rechazadas sin leer
- [ ] `Alta_ArticuloInactivoOInexistente_ErrorEnLaLinea` — línea rechazada, nada grabado (D-14)
- [ ] `Alta_PrecioMenos150_ErrorEnLaLinea` — precio negativo rechazado con el texto de AC-16; validates AC-16
- [ ] `Alta_FallaAlConfirmar_SePropagaSinGrabar` — error al confirmar revierte y se propaga
- [ ] `Obtener_Inexistente_Null` — presupuesto inexistente rechazado como no encontrado
- [ ] `Alta_SeisDatosYUnaLinea_Grabado` — validates AC-01
- [ ] `Alta_SinOpcionales_Grabado` — validates AC-04
- [ ] `Alta_DniConPuntos_Graba23456789` — validates AC-05
- [ ] `Alta_EmailAnaMailCom_Grabado` — validates AC-08
- [ ] `Alta_Ultimo154_Numero155` — validates AC-09
- [ ] `Alta_SinPresupuestos_Numero1` — validates AC-10
- [ ] `Alta_QuedaEnBorrador` — validates AC-12
- [ ] `Alta_Articulo1850_LineaConPrecioDelCatalogo` — validates AC-15 y AC-18 (sin recalcular; Cantidad 1, Descuento 0)
- [ ] `Alta_RelojLocal10_03_2026_GuardaEsaFecha` — validates AC-19, incluido 10/03/2026 23:30 hora local (UTC 11/03) (D-06)
- [ ] `Alta_LeeArticulosNumeroYFechaDentroDeLaOperacion` — ADR-013, D-07
- [ ] `Buscar_SoloActivos_PasaElFiltro` — validates AC-13 y AC-14
- [ ] `Alta_NoRegistraDatosDelCliente` — D-17: un registrador falso no recibe apellido, nombre, DNI, domicilio, email ni teléfono
- [ ] `Catalogo_NoUsaTiposDePresupuestos` — D-05

**Completion criterion**
Las 22 pruebas pasan y `Optica.Application.Tests` completo queda en verde.

## Block 4 — Api: alta y consulta de presupuestos y `soloActivos`

**Files**
- `src/Optica.Api/Controllers/PresupuestosController.cs` (new) — `SolicitudPresupuesto`, `SolicitudCliente`, `SolicitudLinea` (sin `PrintMembers`, D-17) y las dos acciones.
- `src/Optica.Api/Controllers/ArticulosController.cs` (modified) — `[FromQuery] bool? soloActivos` en `Listar`, antes del `CancellationToken`; un error de enlace en `soloActivos` responde 400 en ese campo (hoy cualquier error fuera de `pagina` lanza `InvalidOperationException`).
- `src/Optica.Application/Catalogo/ValidacionArticulo.cs` (modified) — `CampoSoloActivos = "soloActivos"` y `AccionSoloActivosInvalido = "Indicá true o false."` (D-26).
- `tests/Optica.Api.Tests/Catalogo/CierrePorDefectoArticulosTests.cs` (modified) — las llamadas a `controller.Listar` pasan `cancelacion` por nombre; las teorías `Listar_ErrorDeEnlaceFueraDePagina_Lanza` y `Listar_ErrorEnPaginaYEnOtraClave_Lanza` se adaptan para que `soloActivos` no lance.
- `src/Optica.Api/Program.cs` (modified) — registra `AltaPresupuesto` y `ObtenerPresupuesto`.
- `tests/Optica.Api.Tests/Presupuestos/PresupuestosApiTests.cs` y `CierrePorDefectoPresupuestosTests.cs` (new)
- `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs` y `SinDatosArcaTests.cs` (modified) — 401, 415 y 413 del POST; valor de ruta de `GET /api/presupuestos/{numero:long}`.
- `tests/Optica.Api.Tests/Catalogo/ArticulosApiTests.cs` (modified) — `soloActivos`.

**Logic**
Solo traduce HTTP y delega en Block 3. El POST responde 201 con el presupuesto grabado y `Location` hacia su GET. Un
`ModelState` inválido responde 400 con un error fijo en el campo, sin el valor recibido ni el mensaje del lector de
JSON. No registra el cuerpo ni la query string (D-28 de FEAT-002b sigue vigente). Los traductores son cerrados por
defecto: un resultado no previsto lanza y el manejador responde 500 genérico, sin datos del pedido.

**API contract**
- Method + path: `POST /api/presupuestos`
  - Request: `{ cliente: { apellido: string?, nombre: string?, dni: string?, domicilio: string?, email: string?, telefono: string? }, lineas: [{ codigoArticulo: long? }] }`, JSON, hasta 16 KB (D-16); números JSON estrictos
  - Response 201: `{ numero: long, fecha: "yyyy-MM-dd", estado: "borrador", cliente: { apellido, nombre, dni, domicilio, email, telefono }, lineas: [{ codigoArticulo: long, descripcion: string, precioUnitario: number, cantidad: int, porcentajeDescuento: number, precioConDescuento: number, precioFinal: number }] }`, `Location: /api/presupuestos/{numero}`
  - Error codes: 400 (`errors.{campo}` con los campos de D-20, o cuerpo ilegible), 401, 413, 415, 500 (falla al confirmar)
- Method + path: `GET /api/presupuestos/{numero:long}`
  - Request: `numero` en la ruta; sin cuerpo
  - Response 200: el mismo cuerpo que el 201
  - Error codes: 401, 404 (ProblemDetails con `title` fijo, sin datos), 500
- Method + path: `GET /api/articulos?texto={texto}&pagina={pagina}&soloActivos={bool}`
  - Request: `soloActivos` (booleano, opcional, falso si falta); `texto` y `pagina` sin cambios
  - Response 200: la respuesta de FEAT-002b; con `soloActivos=true`, todos los artículos tienen `activo: true`
  - Error codes: 400 (`soloActivos` ilegible, o `texto`/`pagina` como hoy), 401
- Auth: sesión obligatoria en las tres (política por defecto, fuera de `RutasAnonimas`); cookie `SameSite=Strict`, sin CORS.

**Data model**
- Reuses `Presupuesto` and `LineaPresupuesto` from Block 2 — no schema change; constraints as declared there: PK
  `Numero`, NOT NULL, CHECK, FOREIGN KEY a `Presupuesto` y a `Articulo`.

**Input validation**
- Cuerpo: `[JsonNumberHandling(Strict)]`; los textos del cliente como `string?` (un DNI enviado como número es un cuerpo
  ilegible en `dni`); `codigoArticulo` `long?`; tamaño por `RequestSizeLimit` (16 KB) y Content-Type JSON por
  `LimitesDelCuerpo`.
- `numero` de la ruta: restricción `long`; otro texto no coincide con la ruta (404 del enrutamiento con sesión).
- `soloActivos`: booleano; otro valor → 400 en `soloActivos` con texto fijo.

**Error handling**
- Cuerpo ilegible — 400 con un solo error en el campo que nombra la clave del lector, o en `apellido`, y la acción fija `AccionCuerpoIlegibleConfiguracion`.
- Datos inválidos, sin líneas o línea rechazada — 400 con todos los errores de Block 3, sin repetir valores.
- Presupuesto inexistente — 404 con ProblemDetails sin detalles.
- `soloActivos` ilegible — 400 en `soloActivos`.
- Content-Type que no es JSON — 415.
- Cuerpo de más de 16 KB — 413.
- Sin sesión — 401 antes que 415 y 413.
- Resultado no previsto o falla al confirmar — 500 genérico sin datos del pedido.

**Required tests**
- [ ] `Post_CuerpoIlegible_400` — cuerpo ilegible rechazado, sin el valor
- [ ] `Post_SinDni_400ConIngresaElDni` — DNI faltante rechazado; validates AC-02
- [ ] `Post_SinApellidoOSinNombre_400EnElCampo` — faltante rechazado; validates AC-03
- [ ] `Post_Dni123456_123456789_23A56789_400` — DNI inválido rechazado; validates AC-06
- [ ] `Post_EmailAnaArrobaYAnaArrobaMail_400` — email inválido rechazado; validates AC-07
- [ ] `Post_SinLineas_400EnLineas` — presupuesto sin líneas rechazado; validates AC-11
- [ ] `Post_PrecioMenos150_400EnLaLinea` — precio negativo rechazado; validates AC-16
- [ ] `Post_Errores400_NoRepitenElValor` — rechazado sin repetir el DNI ni el email enviados (D-17)
- [ ] `Get_Inexistente_404` — presupuesto inexistente rechazado
- [ ] `Get_Listado_SoloActivosIlegible_400` — parámetro inválido rechazado
- [ ] `Post_SinJson_415` (en `ProteccionTests`) — Content-Type inválido rechazado
- [ ] `Post_DemasiadoGrande_413` (en `ProteccionTests`, 16 KB) — cuerpo inválido rechazado
- [ ] `RutasNuevas_SinSesion_401` (la prueba existente de `ProteccionTests`; sus arrays `nuevas` y `pedidos` suman `POST /api/presupuestos` y `GET /api/presupuestos/{numero}`) — no autorizado
- [ ] `Traductores_ResultadoNoPrevisto_Lanzan` (`CierrePorDefectoPresupuestosTests`) — error no previsto, 500
- [ ] `Post_SeisDatos_201YGetLosMuestra` — validates AC-01 y AC-17
- [ ] `Post_SinOpcionales_201` — validates AC-04
- [ ] `Post_Dni23Punto456Punto789_GetDevuelve23456789` — validates AC-05
- [ ] `Post_EmailAnaMailCom_201` — validates AC-08
- [ ] `Post_Ultimo154_Numero155` — validates AC-09
- [ ] `Post_BaseVacia_Numero1` — validates AC-10
- [ ] `Post_EstadoBorrador` — validates AC-12
- [ ] `Get_Listado_SoloActivosArmazon_SinInactivos` — validates AC-13
- [ ] `Get_Listado_SoloActivosPorCodigo` — validates AC-14
- [ ] `Post_Articulo1850_Linea1850Cantidad1Descuento0` — validates AC-15 y AC-18
- [ ] `Post_Reloj10_03_2026_GetDevuelveEsaFecha` — validates AC-19 (`FakeTimeProvider` de `FabricaApi` con zona horaria de Argentina)
- [ ] `Registro_AltaPresupuesto_SinDatosPersonales` — D-17: con el registro en `Trace`, ningún mensaje contiene los datos del cliente
- [ ] `Solicitudes_ToString_SinValores` — D-17

**Completion criterion**
Las 27 pruebas pasan, `RutasAnonimas` sigue con cinco rutas, `SinDatosArcaTests` revisa `GET /api/presupuestos/{numero:long}`
y `Optica.Api.Tests` queda en verde.

## Block 5 — Web: alta del presupuesto y presupuesto grabado

**Files**
- `web/src/paginas/NuevoPresupuesto.tsx` y `NuevoPresupuesto.test.tsx` (new) — datos del cliente, buscador de artículos activos y tabla de líneas.
- `web/src/paginas/Presupuesto.tsx` y `Presupuesto.test.tsx` (new) — presupuesto grabado, solo lectura.
- `web/src/presupuestos/reglas.ts` y `reglas.test.ts` (new) — las reglas del cliente y sus textos, espejo de `ValidacionPresupuesto`.
- `web/src/api/cliente.ts` y `cliente.test.ts` (modified) — `grabarPresupuesto`, `consultarPresupuesto`, lector `leerPresupuesto`; `buscarArticulos` con `soloActivos`, que viaja en la URL solo cuando es verdadero: el listado del catálogo sigue pidiendo `?pagina=1` y `?texto=...&pagina=1`, así `Articulos.test.tsx` y `App.test.tsx` no cambian por esto.
- `web/src/App.tsx`, `App.test.tsx`, `web/src/paginas/Inicio.tsx` e `Inicio.test.tsx` (modified) — rutas `/presupuestos/nuevo` y `/presupuestos/:numero`; enlace "Nuevo presupuesto".

**Logic**
- `/presupuestos/nuevo`: formulario con Apellido, Nombre, DNI, Domicilio, Email y Nro. de Teléfono; buscador
  ("Texto a buscar" y Buscar) que pide `buscarArticulos(texto, pagina, soloActivos: true)` y lista Código,
  Descripción y Precio de Venta con un botón "Agregar" por resultado (FR-09). Al agregar, si el precio de venta es
  negativo no agrega la línea y muestra junto al buscador "El precio unitario no puede ser negativo" (FR-12, AC-16);
  si no, agrega la línea con Código, Descripción y Precio Unitario del artículo y Cantidad 1, Descuento 0, Precio con
  Descuento y Precio Final iguales al Precio Unitario (FR-10, FR-14). Tabla de líneas con las siete columnas de FR-13;
  el Precio Unitario se muestra en un campo de solo lectura (`readOnly`, FR-11); botón "Quitar" por línea (D-21).
- Grabar envía el cliente y los códigos de artículo; con 201 navega a `/presupuestos/{numero}` con el mensaje de
  confirmación en el estado de la navegación (D-24: "Presupuesto N.º {numero} grabado.
  Estado: Borrador."), que nombra el estado (AC-01, AC-12).
- `/presupuestos/:numero`: número, fecha en formato dd/mm/aaaa (FR-15), estado "Borrador", los seis datos del cliente
  y la tabla de líneas con importes formateados con `mostrarImporte` (NFR-02).
- Mismas reglas de pantalla que el servidor (`presupuestos/reglas.ts`), como comodidad; la validación que vale es la
  del servidor. Los datos del cliente viven solo en el estado de React: nada en `localStorage` ni `sessionStorage`
  (D-17).

**Data model**
- Reuses `Presupuesto` and `LineaPresupuesto` from Block 2 — no schema change; constraints as declared there: PK
  `Numero`, NOT NULL, CHECK de DNI, estado, largos e importes, FOREIGN KEY de la línea.

**Input validation**
- Formulario: las reglas de `presupuestos/reglas.ts` (obligatorios, largos de D-08, DNI de D-09, email de D-10) y al
  menos una línea antes de enviar.
- Búsqueda: hasta 200 caracteres tras el recorte (`validarBusqueda` de `catalogo/reglas.ts`).
- `:numero` de la ruta: entero positivo seguro; si no, el mensaje de no encontrado.
- Respuesta: `leerPresupuesto` exige número entero positivo, fecha `yyyy-MM-dd`, estado `borrador` o `final`, los
  datos del cliente y líneas con importes dentro de `IMPORTE_MAXIMO_SEGURO`.

**Error handling**
- 400 con errores — cada mensaje junto a su campo; `lineas[i]` junto a la línea i; `lineas` junto a la tabla.
- Artículo con precio negativo al agregar — no se agrega y se muestra el mensaje de AC-16 junto al buscador.
- Grabar sin líneas — mensaje junto a la tabla de líneas, sin enviar.
- Número de ruta inválido o 404 — mensaje de presupuesto no encontrado (D-24: "No se encontró el presupuesto.").
- Respuesta ilegible o error de red — mensaje general, sin importes ni datos falsos.
- 413 — mensaje "Los datos enviados son demasiado largos. Revisalos y grabá de nuevo." (el del catálogo).
- 401 — vuelve al ingreso, como el resto de la SPA.

**Required tests**
- [ ] `NuevoPresupuesto_ErroresPorCampo400_JuntoAlCampo` — faltantes rechazados junto al campo; validates AC-02 y AC-03
- [ ] `NuevoPresupuesto_Dni123456_ErrorAntesDeEnviar` — DNI inválido rechazado; validates AC-06
- [ ] `NuevoPresupuesto_EmailAnaArroba_ErrorAntesDeEnviar` — email inválido rechazado; validates AC-07
- [ ] `NuevoPresupuesto_SinLineas_ErrorSinEnviar` — presupuesto sin líneas rechazado; validates AC-11
- [ ] `NuevoPresupuesto_ArticuloMenos150_NoSeAgrega` — precio negativo rechazado junto al buscador; validates AC-16
- [ ] `Presupuesto_NumeroInvalidoO404_NoEncontrado` — presupuesto inexistente rechazado
- [ ] `NuevoPresupuesto_RespuestaIlegible_MensajeGeneral` — error de respuesta ilegible
- [ ] `NuevoPresupuesto_413_Mensaje` — cuerpo grande rechazado
- [ ] `NuevoPresupuesto_SinSesion401_VuelveAlIngreso` — no autorizado
- [ ] `leerPresupuesto_EstadoDesconocido_RespuestaIlegible` (`cliente.test.ts`) — respuesta inválida rechazada
- [ ] `NuevoPresupuesto_Grabar_NavegaYConfirmaBorrador` — validates AC-01 y AC-12
- [ ] `NuevoPresupuesto_SinOpcionales_Graba` — validates AC-04
- [ ] `Presupuesto_MuestraDni23456789` — validates AC-05
- [ ] `NuevoPresupuesto_EmailAnaMailCom_Graba` — validates AC-08
- [ ] `Presupuesto_MuestraNumero155` — validates AC-09 (el número que devuelve la Api)
- [ ] `NuevoPresupuesto_Buscar_PideSoloActivos` — validates AC-13 y AC-14
- [ ] `NuevoPresupuesto_Agregar1850_PrecioUnitarioSoloLectura` — validates AC-15
- [ ] `Presupuesto_Linea_MuestraLasSieteColumnas` — validates AC-17 y AC-18
- [ ] `Presupuesto_MuestraFecha10_03_2026` — validates AC-19
- [ ] `NuevoPresupuesto_NoGuardaDatosEnElAlmacenamiento` — D-17
- [ ] `Inicio_EnlaceNuevoPresupuesto` — D-21

**Completion criterion**
Las 21 pruebas pasan, `npm run test:coverage` y `npm run build` quedan en verde con cobertura ≥ 80 %.

## Block 6 — E2E: AC-01 a AC-19 en Chrome y Edge

**Files**
- `e2e/tests/presupuestos-alta.spec.ts` (new) — los 19 AC sobre la Api y la SPA reales.
- `e2e/README.md` (modified) — proyectos `chrome-presupuestos-alta` (5548) y `msedge-presupuestos-alta` (5554); los de Edge pasan a 5549–5554.
- `docs/instalacion.md` (modified) — las secciones de la carpeta de respaldos y del backup advierten que la base y cada copia contienen datos personales de clientes (DNI, domicilio, email y teléfono) sin cifrar, alcanzados por la Ley 25.326 (R-01 del modelo de amenazas, D-23).

**Logic**
Los proyectos se derivan solos del nombre del archivo. La primera prueba define la contraseña de la base y verifica
AC-10 con la base vacía; las demás ingresan con ella. Cada prueba deja Responsable Inscripto, alícuota 21 y múltiplo
0,01 por la Api y crea sus artículos por la Api con códigos en el proveedor propios (`PR-…`): uno de $1.850,00
(costo 1.850 y margen 0), uno de −$150,00 (costo −100 y margen 50, D-04 de FEAT-002a) y artículos activos e inactivos con "Armazón" y "armazon" en la
descripción solo en la prueba de AC-13. AC-09 graba por la Api hasta que el último número es 154 y graba el siguiente
desde la pantalla. AC-19 compara con la fecha local del día de la PC que corre la Api (D-22). Todos los datos de
clientes son ficticios (D-17).

**Data model**
- Reuses `Presupuesto`, `LineaPresupuesto` and `Articulo` from Block 2 — no schema change; constraints as declared
  there: PK `Numero`, NOT NULL, CHECK, FOREIGN KEY de la línea, `Activo` IN (0, 1).

**Input validation**
- Las pruebas escriben datos válidos salvo en los casos de rechazo (AC-02, AC-03, AC-06, AC-07, AC-11, AC-16); los
  textos y los importes los fija cada prueba.

**Error handling**
- Datos del cliente inválidos — la pantalla muestra el error rechazado junto al campo y no graba.
- Grabar sin líneas — rechazado con el mensaje junto a la tabla de líneas.
- Artículo con precio negativo — no se agrega a la línea y se muestra el mensaje junto al buscador.

**Required tests**
- [ ] `el primer presupuesto de la base recibe el numero 1` — validates AC-10
- [ ] `sin DNI rechazado con Ingresá el DNI del cliente` — validates AC-02
- [ ] `sin apellido o sin nombre rechazado junto al campo faltante` — validates AC-03
- [ ] `DNI 123456, 123456789 o 23A56789 rechazado` — validates AC-06
- [ ] `email ana@ o ana@mail rechazado` — validates AC-07
- [ ] `sin lineas rechazado` — validates AC-11
- [ ] `articulo de -150,00 rechazado: no se agrega a la linea` — validates AC-16
- [ ] `seis datos y una linea graban, confirman y se muestran al reabrir` — validates AC-01 y AC-17
- [ ] `sin domicilio, email ni telefono graba` — validates AC-04
- [ ] `DNI 23.456.789 se guarda y se muestra 23456789` — validates AC-05
- [ ] `email ana@mail.com graba` — validates AC-08
- [ ] `despues del 154 el siguiente es el 155` — validates AC-09
- [ ] `queda en Borrador y el mensaje de confirmacion lo dice` — validates AC-12
- [ ] `buscar armazon lista activos con y sin tilde y ningun inactivo` — validates AC-13
- [ ] `buscar por codigo lista el articulo activo` — validates AC-14
- [ ] `articulo de 1.850,00 completa la linea con precio unitario de solo lectura` — validates AC-15
- [ ] `la linea se graba con cantidad 1, descuento 0 y precios de 1.850,00` — validates AC-18
- [ ] `la fecha es la del dia y se muestra al reabrir` — validates AC-19

**Completion criterion**
Las 18 pruebas pasan en `chrome-presupuestos-alta` y `msedge-presupuestos-alta` con `npm run test:harness`, y la suite
completa de `e2e/` sigue en verde con los puertos nuevos.

## Final verification
Las suites completas (`dotnet test Optica.sln`, `npm run test:coverage` y `npm run build` en `web/`, `npm run test:unidad`
y `npm run test:harness` en `e2e/`) pasan; AC-01 a AC-19 tienen prueba de punta a punta; `HasPendingModelChanges` es
falso; la migración `Presupuestos` no reconstruye `Articulo` (ADR-012); Catálogo no referencia a Presupuestos (D-05);
ningún registro de la Api ni `ToString` contiene datos del cliente (D-17); los textos son los de D-24.
