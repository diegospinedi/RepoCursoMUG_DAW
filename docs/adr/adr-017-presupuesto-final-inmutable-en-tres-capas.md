# ADR-017: Presupuesto Final inmutable en tres capas, con versión para la edición en Borrador

| Field | Value |
|-------|-------|
| Date | 2026-11-11 |
| Ticket | FEAT-003c |
| Status | Accepted |

## Context
FEAT-003c permitió modificar un presupuesto en Borrador y pasarlo a Final de forma irreversible (PRD
FEAT-003c FR-02 a FR-06). Un presupuesto en Final es la base de la factura (PRD 06), y RF-08 y RF-67
de PRD-001 piden que no cambie ni vuelva a Borrador, ni desde la pantalla ni desde un pedido directo a
la Api. El usuario decidió además que dos sesiones que editan el mismo Borrador no se pisen: el segundo
grabado se rechaza con aviso (decisiones-FEAT-003c D-01). La tabla `Presupuesto` de FEAT-003a no tenía
columna de versión, y en SQLite reconstruir una tabla borra los triggers creados a mano (ADR-012).

## Options considered

### Option 1: regla solo en el dominio y el caso de uso
- **Pros:** sin SQL escrito a mano; una sola implementación de la regla.
- **Cons:** cualquier camino que escriba la base sin pasar por el caso de uso (una migración futura,
  un script de datos de prueba, un error en otro repositorio) puede alterar un Final sin que nada lo
  frene.

### Option 2: dominio, caso de uso y triggers de SQLite
- **Pros:** el dominio rechaza la operación, el caso de uso la traduce a un 409 con motivo `final`
  sin escribir, y la base la rechaza aunque alguien saltee las dos capas anteriores; la `Fecha` y el
  `Numero` quedan protegidos también contra un UPDATE directo.
- **Cons:** SQL específico de SQLite en una migración escrita a mano; una reconstrucción futura de la
  tabla tiene que volver a crear los triggers.

## Decision
Se eligió la opción 2. La migración `CierrePresupuestos` agregó `Version INTEGER NOT NULL DEFAULT 0`
con `ALTER TABLE ... ADD COLUMN` (sin reconstruir la tabla) y los triggers que rechazan modificar o
borrar un presupuesto en Final, tocar sus líneas, cambiar `Fecha` o `Numero` y pasar de Final a
Borrador. `Version` quedó como token de concurrencia, con el mismo esquema que el artículo en FEAT-002b
(D-08, D-17, D-19): el pedido trae la versión leída y un desajuste responde 409 `cambiado`.

## Consequences
- Cambian `Presupuesto`, `PresupuestoRepository`, `OpticaDbContext`, `UnidadDeTrabajoEf` (traduce el
  rechazo del trigger a un conflicto `final`), los casos de uso de modificación y paso a Final, el
  controlador y las pantallas.
- Un presupuesto en Final no se puede corregir desde el sistema; el error se corrige con un
  presupuesto nuevo (riesgo aceptado en el PRD).
- La advertencia de `OpticaDbContext` sobre reconstruir tablas ahora protege también estos triggers.
- Para cargar datos de prueba en Final, el presupuesto se graba en Borrador y después pasa a Final,
  como en la aplicación (AGENTS.md).
