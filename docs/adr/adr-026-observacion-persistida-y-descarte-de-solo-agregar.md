# ADR-026: Observación persistida como condición del descarte y descarte de solo agregar

| Field | Value |
|-------|-------|
| Date | 2026-11-14 |
| Ticket | FEAT-008 |
| Status | Accepted |

## Context
El PRD 07 permitía descartar una emisión pendiente solo después del aviso de importe distinto, sin número u otro
entorno, y prohibía descartar una que no se hubiera verificado, también por un pedido directo a la Api (FR-09, FR-11).
La pendiente era inmutable, así que el resultado de la verificación no podía marcarse en ella.

## Options considered

### Option 1: volver a consultar a ARCA al descartar
- **Pros:** sin estado nuevo; el dato de ARCA era el más fresco.
- **Cons:** un descarte directo sin verificar habría pasado si ARCA daba importe distinto (contra AC-10), y con ARCA
  caída no se podía descartar nada.

### Option 2: tabla `ObservacionEmision` grabada por la verificación
- **Pros:** el descarte exigió una verificación previa; el motivo y los importes quedaron para el aviso al reabrir.
- **Cons:** una tabla más, con su trigger y su relación con la pendiente.

## Decision
Option 2. La verificación graba (o reemplaza) la observación; el descarte exige una vigente y vuelve a comprobar el
motivo contra el estado actual. Cada descarte queda en `DescarteEmision`, de solo agregar (triggers `registro_cerrado`
para UPDATE y DELETE).

## Consequences
- Migración `RecuperacionEmisiones` con las dos tablas; `ObservacionEmision` solo se agrega y se quita.
- El registro de descartes no se puede corregir desde el sistema: la conciliación con el contador usa ese registro tal
  como quedó.
