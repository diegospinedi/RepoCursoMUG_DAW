# ADR-012: Migraciones SQLite sin reconstrucción de tabla

| Field | Value |
|-------|-------|
| Date | 2026-11-05 |
| Ticket | FEAT-002b |
| Status | Accepted |

## Context
FEAT-002b agregó a la tabla `Articulo` las columnas `Activo` y `Version`, cada una con DEFAULT y CHECK. SQLite no
admite `ADD CONSTRAINT`, así que EF Core 10, ante `AddCheckConstraint`, reconstruyó la tabla: creó una temporal desde
el modelo, copió las filas, borró la original y la renombró. La migración generada dejó los artículos existentes
inactivos, perdió los DEFAULT declarados fuera del modelo (D-21) y reinició la secuencia AUTOINCREMENT, que FEAT-002a
usa para no reasignar un código. El mismo mecanismo borra los triggers creados con `Sql()`, como los de inmutabilidad
que AGENTS.md anuncia para presupuestos y facturas.

## Options considered

### Option 1: aceptar la migración generada por EF
- **Pros:** sin SQL escrito a mano; el generador y el snapshot coinciden sin revisión.
- **Cons:** artículos existentes inactivos, DEFAULT perdidos y secuencia reiniciada; en tablas con triggers, triggers
  borrados sin aviso.

### Option 2: SQL a mano con `ALTER TABLE … ADD COLUMN`
- **Pros:** `ADD COLUMN` admite DEFAULT y un CHECK de columna sin reconstruir la tabla, y `DROP COLUMN` (SQLite 3.35 o
  posterior; la librería empaquetada es 3.53.3) revierte; los datos, la secuencia y los triggers quedan intactos.
- **Cons:** el SQL de la migración se revisa a mano y el modelo tiene que declarar los mismos CHECK para que
  `HasPendingModelChanges` dé falso.

## Decision
Se eligió la opción 2 para `CatalogoActivo` (D-23). Los cambios de esquema que en SQLite obligaban a reconstruir una
tabla con AUTOINCREMENT, DEFAULT fuera del modelo o triggers se escribieron con `migrationBuilder.Sql`, con una prueba
que verifica los datos, la secuencia y el Down; los CHECK se declararon también en el modelo.

## Consequences
- `20261105104610_CatalogoActivo.cs` usa `ALTER TABLE … ADD COLUMN` y `DROP COLUMN`; su prueba de migración verifica
  `sqlite_sequence`, los CHECK y la reversión.
- `OpticaDbContext.cs` lleva una advertencia junto a `Articulo` para revisar el SQL de cada migración que la toque.
- Una migración futura generada por EF que reconstruya estas tablas reaparece como riesgo si nadie revisa su SQL; la
  prueba de cada migración es el resguardo, no el generador.
