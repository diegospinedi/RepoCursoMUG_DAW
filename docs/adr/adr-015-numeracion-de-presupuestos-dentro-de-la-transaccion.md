# ADR-015: Numeración de presupuestos calculada dentro de la transacción de escritura

| Field | Value |
|-------|-------|
| Date | 2026-11-06 |
| Ticket | FEAT-003a |
| Status | Accepted |

## Context
FEAT-003a numera los presupuestos con el último número más 1, desde 1 (FR-06, decisión D-02), sin repetir
ni saltear números (AC-09, AC-10). La factura (PRD 06) parte de ese número. ADR-013 fijó la transacción
`BEGIN IMMEDIATE` que abarca las lecturas, pero su alcance declarado eran los casos de uso del catálogo.
El detalle está en `docs/ddw/specs/spec-FEAT-003a.md` (Block 2 y Block 3) y en D-07.

## Options considered

### Option 1: `MAX(Numero) + 1` dentro de `EjecutarEnTransaccionAsync`, con `Numero` como clave primaria
- **Pros:** sin huecos, porque solo se calcula al confirmar; `BEGIN IMMEDIATE` toma el bloqueo de escritura
  antes de leer el máximo, así dos altas no leen el mismo número; la clave primaria rechaza un número
  repetido aunque la lógica fallara. Reutiliza ADR-013 sin infraestructura nueva.
- **Cons:** serializa las altas (aceptable con SQLite y una sola PC); depende de que no se borren
  presupuestos, cosa que está fuera de alcance.

### Option 2: `INTEGER PRIMARY KEY AUTOINCREMENT` generado por SQLite
- **Pros:** no hace falta leer el máximo.
- **Cons:** una transacción que vuelve atrás consume el número y deja un hueco; la numeración queda atada a
  `sqlite_sequence`, que ADR-012 evita tocar en las migraciones.

### Option 3: tabla contador con una fila por tipo de comprobante
- **Pros:** sirve para otras numeraciones.
- **Cons:** una tabla y una escritura más por alta, sin una necesidad presente que la justifique.

## Decision
Se eligió la opción 1: es la única de las tres que no deja huecos sin sumar estructura, y la regla pura
`NumeracionPresupuesto.Siguiente` quedó en el dominio, separada de la lectura del máximo. El alcance de
ADR-013 se extendió a los casos de uso de Presupuestos.

## Consequences
- `AltaPresupuesto` leyó el máximo, los artículos y la fecha dentro de la misma transacción.
- La tabla `Presupuesto` tiene `Numero` como clave primaria sin `AUTOINCREMENT` (`ValueGeneratedNever`).
- Un número repetido llega como `ConflictoAlConfirmarException` (motivo `Unicidad`) y responde 500 genérico,
  sin reintento. La prueba con dos sesiones simultáneas quedó para FEAT-003d.
- Si alguna vez se permite borrar presupuestos, el máximo podría bajar y repetir números: esa decisión
  requiere revisar este ADR.
