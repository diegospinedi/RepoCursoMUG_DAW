# Spec FEAT-003c: Edición en Borrador y paso a Final irreversible

| Field | Value |
|-------|-------|
| Ticket | FEAT-003c |
| PRD | docs/ddw/prd/prd-FEAT-003c.md |
| Tier | FEATURE |
| Date | 2026-11-11 |
| Spec loops | 2 |
| Loops since last human decision | 2 |

## Summary
El dominio suma `Presupuesto.Modificar`, `Presupuesto.PasarAFinal` y la versión del presupuesto; un presupuesto en
Final no cambia nunca y la fecha y el número se conservan. La base protege la misma regla con triggers de SQLite en la
migración `CierrePresupuestos`, que además agrega la columna `Version` como token de concurrencia (ADR-017, D-01). La
Api suma `PUT /api/presupuestos/{numero}` para modificar un Borrador y `PUT /api/presupuestos/{numero}/estado` para
pasarlo a Final, con 409 `cambiado` o `final`. La web extrae el formulario del alta a un componente que usan el alta y
la nueva pantalla de modificación, y la pantalla del presupuesto suma "Modificar" y "Pasar a Final" con un diálogo de
confirmación. Las líneas existentes conservan su copia del catálogo (D-02).

## Coverage: PRD → blocks
| Requirement | Covered by |
|---|---|
| FR-01 | Block 1, Block 2, Block 3 |
| FR-02 | Block 1, Block 3, Block 4 |
| FR-03 | Block 1, Block 3, Block 4 |
| FR-04 | Block 4, Block 5 |
| FR-05 | Block 1, Block 2, Block 3, Block 4 |
| FR-06 | Block 1, Block 2, Block 3 |
| FR-07 | Block 1, Block 3, Block 5 |
| NFR-01 | Strategy: `e2e/tests/presupuestos-edicion.spec.ts` en los proyectos Chrome y Edge del harness (Block 5); la versión inmediata anterior de cada navegador queda para FEAT-003d, como en FEAT-003a y FEAT-003b |

## Dependencies between blocks
Block 2 depende de Block 1 (versión y métodos del dominio). Block 3 depende de Block 1 y Block 2. Block 4 depende del
contrato de Block 3. Block 5 depende de Block 3 y Block 4. Orden: 1 → 2 → 3 → 4 → 5.

## Block 1 — Dominio: versión, modificación y paso a Final

**Files**
- `src/Optica.Domain/Presupuestos/Presupuesto.cs` (modified) — propiedad `Version` (long, 0 a 2^53−1) en el
  constructor de reconstrucción; `Nuevo` crea con versión 0; `Modificar(cliente, lineas)` devuelve un presupuesto con el
  mismo `Numero`, `Fecha`, `Estado` Borrador y `Version`, y los datos nuevos; `PasarAFinal()` devuelve el mismo
  presupuesto en Final. Los dos lanzan `InvalidOperationException` con texto fijo si el presupuesto está en Final.
- `src/Optica.Domain/Presupuestos/LineaPresupuesto.cs` (modified) — `ConCantidadYDescuento(cantidad, descuento)`
  devuelve `ResultadoLinea` con la misma copia (código, descripción, Precio Unitario) y los precios recalculados con
  `CalculoLinea`; `ImporteExcedido` si el Precio Final supera el límite del tipo (D-02).
- `src/Optica.Infrastructure/Persistencia/PresupuestoRepository.cs` (modified) — solo la llamada al constructor
  (:128) pasa versión 0 para que la solución compile; la versión real llega en Block 2.
- `tests/Optica.Domain.Tests/Presupuestos/PresupuestoTests.cs` (modified) — pruebas nuevas y llamadas al constructor
  con versión (:79, :82, :111, :139).
- `tests/Optica.Domain.Tests/Presupuestos/LineaPresupuestoTests.cs` (modified) — `ConCantidadYDescuento`.
- `tests/Optica.Application.Tests/Presupuestos/Dobles.cs` (modified) — llamada al constructor (:38).
- `tests/Optica.Application.Tests/Presupuestos/ObtenerPresupuestoTests.cs` (modified) — llamada al constructor (:47).
- `tests/Optica.Infrastructure.Tests/Persistencia/PresupuestoRepositoryTests.cs` (modified) — llamada al constructor
  (:403).

**Logic**
Implements FR-01, FR-02, FR-03, FR-05 y FR-06 en el dominio. `Modificar` valida como el constructor (al menos una
línea, total dentro del límite del tipo) y no toca `Numero`, `Fecha` ni `Version`; quien persiste incrementa la versión.
`PasarAFinal` solo cambia el estado. No existe operación de Final a Borrador. `ConCantidadYDescuento` recalcula con el
Precio Unitario grabado de la línea, nunca con el del catálogo (FR-07, D-02).

**Data model**
- Reuses `Presupuesto` y `LineaPresupuesto` de FEAT-003a — sin cambio de esquema en este bloque; constraints as
  declared there: `Numero` PK ≥ 1, `Fecha` NOT NULL, `Estado` NOT NULL con CHECK `IN ('borrador','final')`, líneas con
  PK `(PresupuestoNumero, Orden)` y FK a `Articulo` `ON DELETE RESTRICT`. `Version` llega a la tabla en Block 2.

**Input validation**
- version: entero de 0 a 2^53−1.
- cantidad y descuento de `ConCantidadYDescuento`: las reglas de `CalculoLinea` (1 a 9.999; 0 a 100 con 2 decimales).

**Error handling**
- Modificar un presupuesto en Final — `InvalidOperationException` con texto fijo, sin cambios.
- Pasar a Final un presupuesto ya en Final — `InvalidOperationException` con texto fijo.
- Versión fuera de rango — `ArgumentException` con texto fijo que nombra el campo.
- Precio Final excedido al recalcular una línea — `ResultadoLinea.ImporteExcedido`, sin línea.

**Required tests**
- [ ] `Modificar_Borrador_ConservaNumeroFechaYVersion` — validates AC-01, AC-02
- [ ] `Modificar_Final_Rechazado` — validates AC-07 (sad path)
- [ ] `PasarAFinal_Borrador_QuedaEnFinal` — validates AC-04
- [ ] `PasarAFinal_Final_Rechazado` — validates AC-08 (sad path)
- [ ] `Constructor_VersionInvalida_Rechazada` — -1 y 2^53 rechazados (sad path)
- [ ] `ConCantidadYDescuento_ConservaPrecioUnitarioGrabado` — validates AC-09
- [ ] `ConCantidadYDescuento_ImporteExcedido_Rechazado` — Precio Final por encima del límite (sad path)

**Completion criterion**
`dotnet build Optica.sln` compila y `dotnet test Optica.sln` pasa con las pruebas nuevas y las llamadas al constructor
actualizadas.

## Block 2 — Persistencia: versión, triggers de cierre y actualización

**Files**
- `src/Optica.Application/Comun/IUnidadDeTrabajo.cs` (modified) — motivo genérico `MotivoConflicto.RegistroCerrado`
  (sirve también para las facturas con CAE de PRD 06) y, en la documentación de `ConfirmarAsync`, la convención: un
  trigger que protege un registro cerrado aborta con `RAISE(ABORT, 'registro_cerrado')`.
- `src/Optica.Infrastructure/Persistencia/OpticaDbContext.cs` (modified) — `PresupuestoFila.Version` (long, NOT NULL)
  con `IsConcurrencyToken()` y `HasCheckConstraint("CK_Presupuesto_Version", "\"Version\" BETWEEN 0 AND
  9007199254740991")`, el mismo CHECK que escribe la migración (patrón `CatalogoActivo`, ADR-012); advertencia ADR-012
  en la configuración de `Presupuesto` y `LineaPresupuesto`, como la de `Articulo` (:94-98): reconstruir esas tablas
  borra los triggers de cierre.
- `src/Optica.Infrastructure/Persistencia/Migraciones/{marca}_CierrePresupuestos.cs` (new) y su `.Designer.cs` (new) —
  `ALTER TABLE "Presupuesto" ADD COLUMN "Version" INTEGER NOT NULL DEFAULT 0 CONSTRAINT "CK_Presupuesto_Version" CHECK
  ("Version" BETWEEN 0 AND 9007199254740991)` y los triggers, con `migrationBuilder.Sql`. `Down` elimina primero los
  seis triggers y después la columna; un comentario dice que es solo para desarrollo y que deja sin protección a los
  Final grabados (AGENTS.md: no se borran en la base de la óptica).
- `src/Optica.Infrastructure/Persistencia/Migraciones/OpticaDbContextModelSnapshot.cs` (modified) — columna `Version`
  y su CHECK.
- `src/Optica.Domain/Presupuestos/IPresupuestoRepository.cs` (modified) — `Actualizar(original, modificado,
  versionOriginal)` y `CambiarEstado(presupuesto, versionOriginal)`, que toma `Numero` y `Estado` del presupuesto del
  dominio ya pasado a Final.
- `src/Optica.Infrastructure/Persistencia/PresupuestoRepository.cs` (modified) — `ObtenerAsync` lee `Version` y trata como estado corrupto
  una lista de `Orden` que no sea exactamente 1..n (el `orden` de la Api es la posición + 1 de la línea, D-03);
  `Actualizar` adjunta la fila con la versión original, escribe cliente y `Version + 1`, marca las líneas de `original`
  como borradas y agrega las de `modificado` con `Orden` 1..n, todo en el mismo `SaveChanges` (nunca
  `ExecuteDelete`/`ExecuteUpdate` en este camino); `CambiarEstado` adjunta solo `PresupuestoFila` y marca modificados
  únicamente `Estado` y `Version` (`UPDATE Presupuesto SET Estado, Version WHERE Numero AND Version`), sin tocar las
  líneas. Los dos rechazan `versionOriginal` igual a 2^53−1 con `ArgumentException`; `CambiarEstado` rechaza además un
  presupuesto que no esté en Final.
- `src/Optica.Infrastructure/Persistencia/UnidadDeTrabajoEf.cs` (modified) — `catch (DbUpdateException e) when
  (e.InnerException is SqliteException { SqliteExtendedErrorCode: 1811 } s && s.Message.Contains("'registro_cerrado'",
  StringComparison.Ordinal))` antes del `catch` genérico: `ChangeTracker.Clear()` y
  `ConflictoAlConfirmarException(MotivoConflicto.RegistroCerrado)` sin causa ni entradas, como `CK_CredencialAcceso_Id`.
- `tests/Optica.Application.Tests/Presupuestos/Dobles.cs` (modified) — `PresupuestosEnMemoria` implementa `Actualizar`
  y `CambiarEstado` y simula los conflictos `Concurrencia` y `RegistroCerrado`.
- `tests/Optica.Infrastructure.Tests/Persistencia/PresupuestoRepositoryTests.cs` (modified) — pruebas nuevas; la lista
  de migraciones termina en `_CierrePresupuestos` (:531-535); las columnas incluyen `Version` (:591-595); la
  aserción sin `DEFAULT` (:608) excluye `Version`; la de "sin triggers" (:656-657) pasa a verificar los triggers; el
  caso de fecha corrupta (:58) se arma con un INSERT directo en lugar de un UPDATE; el presupuesto en Final (:401-403)
  se graba en Borrador y después pasa a Final (AGENTS.md).
- `tests/Optica.Infrastructure.Tests/Acceso/AccesoPersistenciaTests.cs` (modified) — la lista de migraciones
  (:386-390) suma `CierrePresupuestos`.
- `tests/Optica.Infrastructure.Tests/Persistencia/ArticuloRepositoryTests.cs` (modified) — la última migración
  (:361) es `_CierrePresupuestos`.
- `tests/Optica.Infrastructure.Tests/Persistencia/UnidadDeTrabajoTests.cs` (modified) — traducción del trigger.

**Logic**
Implements FR-01, FR-05 y FR-06 en la base y la concurrencia de D-01. Triggers (`RAISE(ABORT, 'registro_cerrado')`
salvo indicación):
- `TR_Presupuesto_FinalInmutable` — BEFORE UPDATE ON `Presupuesto` (sin `OF`, cubre todas las columnas) WHEN
  `OLD.Estado = 'final'`.
- `TR_Presupuesto_FinalNoSeBorra` — BEFORE DELETE ON `Presupuesto` WHEN `OLD.Estado = 'final'`.
- `TR_Presupuesto_FechaYNumeroFijos` — BEFORE UPDATE ON `Presupuesto` WHEN `NEW.Fecha IS NOT OLD.Fecha OR NEW.Numero IS
  NOT OLD.Numero` (`IS NOT`, así reescribir el mismo valor no dispara) con `RAISE(ABORT, 'presupuesto_fecha_numero')`.
- `TR_LineaPresupuesto_FinalInsert` — BEFORE INSERT WHEN el presupuesto de `NEW.PresupuestoNumero` está en Final.
- `TR_LineaPresupuesto_FinalUpdate` — BEFORE UPDATE WHEN el presupuesto de `OLD.PresupuestoNumero` o el de
  `NEW.PresupuestoNumero` está en Final.
- `TR_LineaPresupuesto_FinalDelete` — BEFORE DELETE WHEN el presupuesto de `OLD.PresupuestoNumero` está en Final.
El paso Borrador → Final usa `CambiarEstado`: un único UPDATE del encabezado con `OLD.Estado = 'borrador'`, sin
comandos sobre las líneas, así ningún trigger lo rechaza sea cual sea el orden de EF. Final → Borrador cae en
`TR_Presupuesto_FinalInmutable`. `Actualizar` y `CambiarEstado` corren dentro de `EjecutarEnTransaccionAsync`
(ADR-013).

**Data model**
- Entity `Presupuesto`: suma `Version` INTEGER NOT NULL DEFAULT 0 con CHECK `CK_Presupuesto_Version` `BETWEEN 0 AND
  9007199254740991` (el DEFAULT solo en la migración, para las filas existentes, D-21 de FEAT-002b); token de
  concurrencia. Resto de constraints as declared in FEAT-003a: `Numero` PK, `Fecha` NOT NULL, `Estado` CHECK `IN
  ('borrador','final')`, seis datos del cliente con sus CHECK de largo.
- Entity `LineaPresupuesto`: sin columnas nuevas; constraints as declared in FEAT-003a y FEAT-003b: PK
  `(PresupuestoNumero, Orden)`, `Cantidad` CHECK `>= 1`, descuento CHECK `BETWEEN 0 AND 10000`, importes CHECK `>= 0`,
  FK a `Presupuesto` y a `Articulo` `ON DELETE RESTRICT`.
- Triggers de cierre listados en Logic; la tabla no se reconstruye (ADR-012, ADR-017).
- Rollback: `Down` elimina primero los seis triggers (`DROP TRIGGER IF EXISTS`; SQLite rechaza `DROP COLUMN` de una
  columna que un trigger usa) y después la columna (`ALTER TABLE Presupuesto DROP COLUMN Version`, SQLite ≥ 3.35, sin
  reconstruir la tabla). Es solo para desarrollo: deja sin protección a los presupuestos en Final y no se aplica sobre la
  base de la óptica. Antes de migrar una base con datos reales vale el backup de RNF-02 (`VACUUM INTO`).

**Input validation**
- versionOriginal de `Actualizar` y `CambiarEstado`: la versión leída por el caso de uso, de 0 a 2^53−2; EF la usa como
  valor original del token.
- original y modificado de `Actualizar`: el mismo `Numero`.

**Error handling**
- Versión distinta en la base — `DbUpdateConcurrencyException` → `ConflictoAlConfirmarException(Concurrencia)`, sin
  escribir encabezado ni líneas.
- Escritura sobre un presupuesto en Final o sus líneas — trigger `registro_cerrado` →
  `ConflictoAlConfirmarException(RegistroCerrado)`, sin escribir y sin adjuntar la causa.
- Cambio de `Fecha` o `Numero` — trigger `presupuesto_fecha_numero`; se propaga como error (la aplicación nunca lo
  intenta).
- Borrar un presupuesto en Final — trigger `registro_cerrado`.
- versionOriginal en el máximo, o `CambiarEstado` con un presupuesto que no está en Final — `ArgumentException` con
  texto fijo.
- Lista de `Orden` con huecos al leer — estado corrupto (`InvalidOperationException`), como `AEntidad` hoy.

**Required tests**
- [ ] `Actualizar_Borrador_MenosLineas_ReemplazaLineasEIncrementaVersion` — validates AC-02, AC-03
- [ ] `Actualizar_Borrador_MasLineas_ReemplazaLineasEIncrementaVersion` — validates AC-03
- [ ] `Actualizar_VersionVieja_ConflictoConcurrencia` — conflict: encabezado y líneas intactos (sad path)
- [ ] `CambiarEstado_Borrador_FinalSinTocarLineas` — 100 líneas idénticas después del paso, validates AC-04
- [ ] `CambiarEstado_VersionVieja_ConflictoConcurrencia` — conflict sin escribir (sad path)
- [ ] `Actualizar_VersionMaxima_Rechazada` — invalid (sad path)
- [ ] `CambiarEstado_PresupuestoNoFinal_Rechazado` — invalid (sad path)
- [ ] `Obtener_OrdenConHueco_EstadoCorrupto` — una lista de `Orden` que no es 1..n se rechaza, error (sad path)
- [ ] `Trigger_ModificarFinal_Rechazado` — UPDATE directo de un Final rechazado, validates AC-07 (sad path)
- [ ] `Trigger_LineasDeFinal_Rechazado` — INSERT, UPDATE (también moviendo la línea hacia o desde un Final) y DELETE
  rechazados (sad path)
- [ ] `Trigger_FinalABorrador_Rechazado` — validates AC-08 (sad path)
- [ ] `Trigger_CambiarFechaONumero_Rechazado` — sobre un Borrador, para un mensaje determinista; reescribir la misma
  fecha no dispara; validates AC-01 (sad path)
- [ ] `Trigger_BorrarFinal_Rechazado` — DELETE de un Final rechazado (sad path)
- [ ] `Confirmar_TriggerRegistroCerrado_ConflictoSinCausa` — conflict (sad path)
- [ ] `Migracion_CierrePresupuestos_BorradoresExistentesConVersion0` — la migración sobre una base con presupuestos no
  reconstruye la tabla y deja `Version` 0
- [ ] `Migracion_Down_QuitaTriggersYColumna` — en una base de prueba (sad path cubierto: sin error al quitar la columna)

**Completion criterion**
`dotnet test Optica.sln` pasa, incluidas las pruebas de migraciones y de esquema actualizadas, y
`dotnet ef migrations has-pending-model-changes` no reporta cambios.

## Block 3 — Aplicación y Api: modificar y pasar a Final

**Files**
- `src/Optica.Application/Presupuestos/ModificarPresupuesto.cs` (new) — caso de uso y el tipo de resultado
  `ResultadoEdicionPresupuesto` (`Grabado`, `Invalido`, `NoEncontrado`, `Final`, `Cambiado`) compartido con el paso a
  Final. Antes de leer la base valida versión, cliente, cantidad, descuento y la forma de `orden` (≥ 1, sin repetir);
  dentro de `EjecutarEnTransaccionAsync` lee el presupuesto (inexistente → no encontrado; Final → final; versión
  distinta → cambiado) y recién entonces valida que cada `orden` exista y tenga el mismo código grabado; arma las
  líneas (con `orden`: la línea grabada con `ConCantidadYDescuento`; sin `orden`: `LineaPresupuesto.DesdeArticulo` con
  el artículo activo de `Optica.Domain.Catalogo.IArticuloRepository`), `ControlarTopes`, `Presupuesto.Modificar`,
  `Actualizar(original, modificado, version)` y confirma. Atrapa `ConflictoAlConfirmarException` filtrando por motivo:
  `Concurrencia` relee y decide entre no encontrado, final y cambiado; `RegistroCerrado` → final; otro motivo se
  propaga. Devuelve el presupuesto releído.
- `src/Optica.Application/Presupuestos/PasarPresupuestoAFinal.cs` (new) — caso de uso con el mismo esquema: lee,
  decide inexistente, Final (también para `estado` "borrador") o versión distinta, `PasarAFinal`,
  `CambiarEstado(presupuesto, version)`, relectura. La respuesta a `estado` "borrador" depende del estado leído: sobre
  un Final es final (409), sobre un Borrador es inválido en `estado` (400).
- `src/Optica.Application/Presupuestos/ValidacionPresupuesto.cs` (modified) — `CampoVersion` "version",
  `AccionVersionInvalida`, `CampoEstado` "estado", `AccionLineaAjena`, y los textos fijos de `detail` de los 409
  (`DetalleCambiado`, `DetalleFinal`); validación sintáctica de `orden` y validación semántica contra las líneas
  grabadas (la llama el caso de uso dentro de la transacción); reutiliza las reglas del alta.
- `src/Optica.Application/Presupuestos/AltaPresupuesto.cs` (modified) — `PresupuestoGrabado.Version` (`required`,
  copiada del dominio, como `Total`) y `Orden` en la línea grabada de la respuesta.
- `src/Optica.Application/Presupuestos/ObtenerPresupuesto.cs` (modified) — solo documentación: la versión llega por
  `PresupuestoGrabado.Desde`.
- `src/Optica.Api/Controllers/PresupuestosController.cs` (modified) — `PUT {numero}` y `PUT {numero}/estado`,
  solicitudes `SolicitudModificacionPresupuesto` y `SolicitudEstadoPresupuesto` con `PrintMembers => false`,
  `SolicitudLinea` con `Orden` opcional, traductores `ResponderModificacion` y `ResponderPasoAFinal` cerrados por
  defecto, `CampoDeLaClave` para `version` y `lineas[i].orden`.
- `src/Optica.Api/Errores/RespuestasValidacion.cs` (modified) — helper privado `Conflicto409(titulo, motivo, detalle)`
  que usan `ConflictoArticulo` (firma pública sin cambios) y el nuevo `ConflictoPresupuesto(motivo, detalle)` con título
  fijo "No se pudo grabar el presupuesto."; reutiliza `MotivoCambiado` y suma `MotivoFinal` "final".
- `src/Optica.Api/Program.cs` (modified) — registra `ModificarPresupuesto` y `PasarPresupuestoAFinal` (:148-151).
- `tests/Optica.Application.Tests/Presupuestos/ModificarPresupuestoTests.cs` (new).
- `tests/Optica.Application.Tests/Presupuestos/PasarPresupuestoAFinalTests.cs` (new).
- `tests/Optica.Application.Tests/Presupuestos/AltaPresupuestoTests.cs` (modified) — `{ Total = 0m, Version = 0 }`
  (:560-561).
- `tests/Optica.Application.Tests/Presupuestos/ObtenerPresupuestoTests.cs` (modified) — `Version` en la respuesta.
- `tests/Optica.Api.Tests/Presupuestos/PresupuestosApiTests.cs` (modified) — pruebas de los dos `PUT`.
- `tests/Optica.Api.Tests/Presupuestos/CierrePorDefectoPresupuestosTests.cs` (modified) — constructor del controlador
  (:132), cierre por defecto de los traductores nuevos, claves `version` y `lineas[i].orden`, `ToString` sin valores.
- `tests/Optica.Api.Tests/Acceso/ProteccionTests.cs` (modified) — los dos `PUT` en la lista de rutas protegidas
  (:87-94) y los pedidos sin sesión (:105-121).
- `tests/Optica.Api.Tests/Acceso/SinCacheTests.cs` (modified) — `no-store` de los dos `PUT` (:76-85) y la ruta de la
  SPA `/presupuestos/1/editar` (:172).

**Logic**
Implements FR-01 a FR-07 en la Api. Respeta `DependenciasDeModulosTests`: `Optica.Application.Presupuestos` no usa
`Optica.Application.Catalogo` (tampoco sus constantes); sí usa `Optica.Domain.Catalogo.IArticuloRepository`, como el
alta. Las líneas se renumeran 1..n en el orden recibido, y la respuesta trae el `orden` de cada línea. AC-01: la modificación nunca recalcula la fecha (prueba con
`TimeProvider` falso: alta el 10/03/2026, modificación el 12/03/2026).

**API contract**
- Method + path: `PUT /api/presupuestos/{numero:long}` y `PUT /api/presupuestos/{numero:long}/estado`.
- Request (modificar): `version` (long, obligatorio), `cliente` (los seis datos del alta), `lineas[]` con `orden`
  (int, opcional), `codigoArticulo` (long), `cantidad` (int), `porcentajeDescuento` (decimal); precios ignorados.
- Request (estado): `estado` (string, único valor admitido "final"), `version` (long, obligatorio).
- Response: 200 con el presupuesto releído: `numero`, `fecha`, `estado`, `version`, `cliente`, `lineas` (cada una con
  `orden` además de sus siete columnas), `total`; `GET /api/presupuestos/{numero}` suma `version` y `orden`.
- Error codes: 400 (errores por campo: `version`, `estado`, `cliente.*`, `lineas`, `lineas[i]`, `lineas[i].orden`,
  `lineas[i].cantidad`, `lineas[i].porcentajeDescuento`), 401, 404, 409 con `motivo` `cambiado` o `final`, 413, 415.
- Auth: sesión obligatoria (política por defecto, FEAT-001a); `Cache-Control: no-store`.

**Data model**
- Reuses `Presupuesto` (con `Version` de Block 2) y `LineaPresupuesto` — no schema change; constraints as declared in
  Block 2: `Version` NOT NULL token de concurrencia, `Estado` CHECK `IN ('borrador','final')`, PK de línea
  `(PresupuestoNumero, Orden)`, FK `ON DELETE RESTRICT`, triggers de cierre.

**Input validation**
- version: long de 0 a 2^53−1, obligatorio; faltante o fuera de rango → `version` con "Volvé a abrir el presupuesto
  para editarlo.".
- estado: solo "final"; otro valor → 400 en `estado`, salvo "borrador" sobre un Final, que responde 409 `final`.
- orden: entero ≥ 1, único en el pedido, de una línea existente y con el mismo `codigoArticulo`; si no → 400 en
  `lineas[i]` con "La línea no corresponde a este presupuesto.".
- cliente, cantidad, descuento, cantidad de líneas y topes: las mismas reglas y textos del alta (FEAT-003a, FEAT-003b).
- Cuerpo hasta 16 KB, JSON obligatorio, números estrictos.

**Error handling**
- Presupuesto inexistente — 404 con título fijo.
- Presupuesto en Final — 409 `final`, sin escribir (AC-07, AC-08).
- Versión vieja — 409 `cambiado`, sin escribir.
- Datos inválidos — 400 con todos los errores por campo, sin escribir.
- Artículo nuevo inexistente o inactivo — 400 en `lineas[i]` como en el alta.
- Línea ajena (`orden` inválido o código distinto) — 400 en `lineas[i]`.
- Sin sesión — 401; cuerpo grande — 413; no JSON — 415.

**Required tests**
- [ ] `Modificar_ConRelojPosterior_ConservaFecha` — validates AC-01
- [ ] `Put_ModificarDomicilio_200YGetLoMuestra` — validates AC-02
- [ ] `Put_AgregarYQuitarLinea_200` — validates AC-03
- [ ] `PutEstado_Final_200` — validates AC-04
- [ ] `Put_PresupuestoFinal_409Final` — modificación rechazada, validates AC-07 (sad path)
- [ ] `PutEstado_BorradorSobreFinal_409Final` — validates AC-08 (sad path)
- [ ] `Put_LineaExistente_ConservaPrecioUnitarioGrabado` — validates AC-09
- [ ] `Put_LineaConArticuloInactivo_ConservaCopia` — validates AC-10
- [ ] `Put_VersionVieja_409Cambiado` — conflict (sad path)
- [ ] `PutEstado_VersionVieja_409Cambiado` — conflict (sad path)
- [ ] `Put_VersionFaltante_400` — missing (sad path)
- [ ] `PutEstado_EstadoInvalido_400` — invalid (sad path)
- [ ] `Put_LineaAjena_400` — orden repetido, inexistente o con otro código rechazado (sad path)
- [ ] `Put_OrdenDeVersionVieja_409Cambiado` — la versión se compara antes que el `orden`, conflict (sad path)
- [ ] `PutEstado_BorradorSobreBorrador_400` — `estado` "borrador" sobre un Borrador, invalid (sad path)
- [ ] `Get_LineasTraenOrden` — cada línea de la respuesta trae su `orden` 1..n
- [ ] `Put_ArticuloNuevoInactivo_400` — rejected (sad path)
- [ ] `Put_DatosInvalidos_400PorCampo` — invalid (sad path)
- [ ] `Put_Inexistente_404` — missing (sad path)
- [ ] `Put_SinSesion_401` — unauthorized (sad path)
- [ ] `Put_Errores409_NoRepitenDatos` — el cuerpo del 409 no contiene datos del pedido, error (sad path)

**Completion criterion**
`dotnet test Optica.sln` pasa con las pruebas nuevas y las de protección, caché y cierre por defecto actualizadas.

## Block 4 — Web: pantalla de modificación y paso a Final

**Files**
- `web/src/api/cliente.ts` (modified) — `PresupuestoGrabado.version` (obligatoria en `leerPresupuesto`), `orden`
  obligatorio en cada línea leída (`leerLinea`, entero ≥ 1), `'final'` en `MotivoConflicto` (:93) y en
  `conflictoConMotivo` (:278), `LineaAGrabar.orden` opcional, `modificarPresupuesto(numero,
  cliente, lineas, version)` y `pasarPresupuestoAFinal(numero, version)`.
- `web/src/presupuestos/reglas.ts` (modified) — `MENSAJE_PRESUPUESTO_CAMBIADO` "El presupuesto cambió mientras lo
  editabas. Volvé a abrirlo.", `MENSAJE_PRESUPUESTO_FINAL` "El presupuesto está en Final y no se puede modificar.",
  `TEXTO_CONFIRMAR_FINAL` "Una vez en Final, el presupuesto no se puede modificar.", `mensajePasadoAFinal(n)`
  "Presupuesto N.º {n} pasado a Final."; `mensajeConfirmacion` sigue para el grabado en Borrador.
- `web/src/presupuestos/FormularioPresupuesto.tsx` (new) — el formulario extraído de `NuevoPresupuesto.tsx`: datos del
  cliente, buscador, tabla de líneas con cantidad y descuento, total, mensajes por campo y foco al grabar; recibe los
  valores iniciales y la función de grabado.
- `web/src/paginas/NuevoPresupuesto.tsx` (modified) — usa `FormularioPresupuesto`; sin cambios de comportamiento.
- `web/src/paginas/EditarPresupuesto.tsx` (new) — ruta `/presupuestos/:numero/editar`: carga el presupuesto; en
  Borrador muestra el formulario con los datos y las líneas grabadas (Precio Unitario grabado, `orden`), graba con la
  versión cargada y navega a `/presupuestos/{n}` con `mensajeConfirmacion`; en Final muestra `MENSAJE_PRESUPUESTO_FINAL`
  sin formulario; 409 `cambiado` o `final` muestran los mensajes de la pantalla, nunca el texto del servidor.
- `web/src/paginas/Presupuesto.tsx` (modified) — en Borrador, link "Modificar" y botón "Pasar a Final" que abre un
  `<dialog>` con `TEXTO_CONFIRMAR_FINAL`, botones "Pasar a Final" y "Cancelar", foco en el diálogo y retorno del foco al
  cerrar; confirmar llama a `pasarPresupuestoAFinal` con la versión mostrada y muestra `mensajePasadoAFinal`;
  cancelar no llama a la Api. En Final no muestra acciones de edición.
- `web/src/App.tsx` (modified) — ruta `/presupuestos/:numero/editar`.
- `web/src/api/cliente.test.ts`, `web/src/presupuestos/reglas.test.ts`, `web/src/paginas/NuevoPresupuesto.test.tsx`,
  `web/src/paginas/Presupuesto.test.tsx` (modified) y `web/src/paginas/EditarPresupuesto.test.tsx` (new) — pruebas;
  los fixtures de presupuesto suman `version`.
- `web/src/App.test.tsx` (modified) — `PANTALLAS` (:262-300, :329-332) y el `it.each` (:37-45) suman la ruta nueva;
  fixture con `version` (:292).

**Logic**
Implements FR-02 a FR-05 en la pantalla. Una línea agregada en la modificación toma el precio del catálogo y se envía
sin `orden`; una línea grabada se envía con su `orden` y no puede cambiar de artículo (para cambiarlo se quita y se
agrega otra).

**Data model**
- Reuses `Presupuesto` y `LineaPresupuesto` from Block 2 — no schema change; constraints as declared there: `Version`
  NOT NULL token de concurrencia, `Estado` CHECK `IN ('borrador','final')`, PK de línea `(PresupuestoNumero, Orden)`,
  FK `ON DELETE RESTRICT`, triggers de cierre. La web solo lee y envía.

**Input validation**
- Los mismos campos y reglas del alta (cliente, cantidad con punto de miles, descuento) con `reglas.ts`.
- version: la que devolvió el `GET`; nunca la escribe la operadora.

**Error handling**
- 409 `cambiado` — `MENSAJE_PRESUPUESTO_CAMBIADO`, sin perder lo escrito: lo escrito queda solo en la memoria de la
  pantalla, nunca en `localStorage`/`sessionStorage`, en la URL ni en la consola.
- 409 `final` — `MENSAJE_PRESUPUESTO_FINAL`.
- 400 por campo — mensajes junto a cada campo, como el alta.
- 404 — `MENSAJE_PRESUPUESTO_NO_ENCONTRADO`.
- Respuesta ilegible (sin `version` o una línea sin `orden`) — error de lectura, como `leerPresupuesto` hoy.

**Required tests**
- [ ] `Editar_CargaDatosYLineasGrabadas` — validates AC-02, AC-09
- [ ] `Editar_ModificarDomicilio_GrabaYConfirma` — validates AC-02
- [ ] `Editar_AgregarYQuitarLinea_EnviaOrdenSoloEnLasGrabadas` — validates AC-03
- [ ] `Presupuesto_PasarAFinal_PideConfirmacion` — validates AC-05
- [ ] `Presupuesto_ConfirmarFinal_GrabaFinal` — validates AC-04
- [ ] `Presupuesto_CancelarFinal_NoLlamaALaApi` — validates AC-06
- [ ] `Presupuesto_Final_SinAccionesDeEdicion` — validates AC-07
- [ ] `Editar_PresupuestoFinal_MuestraAvisoSinFormulario` — validates AC-07, AC-08 (sad path, rejected)
- [ ] `Editar_409Cambiado_MuestraMensaje` — conflict (sad path)
- [ ] `Editar_409Final_MuestraMensaje` — conflict (sad path)
- [ ] `Editar_DatoInvalido_MensajeJuntoAlCampo` — invalid (sad path)
- [ ] `Editar_404_MensajeNoEncontrado` — missing (sad path)
- [ ] `leerPresupuesto_SinVersion_ErrorDeLectura` — invalid (sad path)
- [ ] `leerPresupuesto_LineaSinOrden_ErrorDeLectura` — invalid (sad path)

**Completion criterion**
En `web/`, `npm run lint`, `npm run build`, `npm run test:coverage` y `npm run test:lint` pasan, y las pruebas del
alta siguen iguales.

## Block 5 — Punta a punta en Chrome y Edge

**Files**
- `e2e/tests/presupuestos-edicion.spec.ts` (new) — escenarios de AC-01 a AC-10; los datos se crean por la Api en cada
  prueba (artículos propios, presupuestos en Borrador que pasan a Final como en la app); DNI ficticio 99.999.999.
- `e2e/README.md` (modified) — tabla de puertos (:15-41: Chrome `presupuestos-edicion` 5550, Edge 5551 a 5558) y la
  lista `chromium-*` (:165-166).

**Logic**
Implements NFR-01 para esta parte: el recorrido en Chrome y Edge modifica un Borrador, pasa a Final con confirmación y
cancelación, intenta modificar un Final por la pantalla y por la Api, y comprueba que las líneas conservan sus precios
tras recalcular y desactivar el artículo. AC-01 se comprueba en el e2e como fecha intacta después de modificar (la fecha
de otro día se prueba en Block 3 con reloj falso).

**Data model**
- Reuses `Presupuesto` y `LineaPresupuesto` from Block 2 — no schema change; constraints as declared there: `Version`
  NOT NULL token de concurrencia, `Estado` CHECK `IN ('borrador','final')`, PK de línea `(PresupuestoNumero, Orden)`,
  FK `ON DELETE RESTRICT`, triggers de cierre. Cada proyecto del harness usa su base temporal.

**Input validation**
- Los mismos campos de Block 4, cargados desde el navegador.

**Error handling**
- Presupuesto en Final — el e2e verifica el rechazo y que nada cambió (pantalla y Api).
- Cancelación del paso a Final — el e2e verifica que sigue en Borrador.

**Required tests**
- [ ] `AC-01: modificar no cambia la fecha` — validates AC-01
- [ ] `AC-02: modificar el domicilio graba y se ve al reabrir` — validates AC-02
- [ ] `AC-03: agregar una línea y quitar otra` — validates AC-03
- [ ] `AC-04 y AC-05: pasar a Final pide confirmación y graba Final` — validates AC-04, AC-05
- [ ] `AC-06: cancelar deja el presupuesto en Borrador` — validates AC-06; el paso a Final cancelado queda rechazado
  y el presupuesto sigue en Borrador (sad path)
- [ ] `AC-07 y AC-08: un Final rechaza cambios por pantalla y por Api` — validates AC-07, AC-08 (sad path, rejected)
- [ ] `AC-09 y AC-10: las líneas conservan precio tras recalcular y desactivar` — validates AC-09, AC-10

**Completion criterion**
`npm run test:harness` de `e2e/` pasa en Chrome y Edge, incluidos los archivos existentes.

## Final verification
Los 10 AC del PRD tienen pruebas en verde en .NET, web y e2e; `dotnet test Optica.sln`, las pruebas de la web y el
harness de e2e pasan; la base rechaza por trigger toda escritura sobre un presupuesto en Final.
