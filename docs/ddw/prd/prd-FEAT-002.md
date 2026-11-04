# Parent PRD: Catálogo de artículos y precio de venta

| Metric | Value |
|--------|-------|
| Ticket | FEAT-002 |
| Date | 2026-11-04 |
| Status | Split |
| Acceptance criteria to cover | 22 |

## Sub-tickets

| Sub-ticket | Title | PRD | ACs | Dependencies | Status |
|---|---|---|---|---|---|
| FEAT-002a | Alta de artículos con precio de venta calculado | prd-FEAT-002a.md | AC-01, AC-02, AC-03, AC-04, AC-05, AC-06, AC-07, AC-08, AC-10, AC-11, AC-12 | none | active |
| FEAT-002b | Búsqueda, edición y baja de artículos | prd-FEAT-002b.md | AC-13, AC-14, AC-15, AC-19, AC-20, AC-21 | depends on a | pending |
| FEAT-002c | Recálculo del catálogo por configuración y volumen | prd-FEAT-002c.md | AC-09, AC-16, AC-17, AC-18, AC-22 | depends on a and b | pending |

AC-01 a AC-22 vienen de `prd-DISC-001-02.md` (PRD 02 de DISC-001), sin criterios agregados en DEFINE.

Los AC de esta tabla son los números globales. Cada sub-PRD numera sus criterios desde AC-01 y cita junto
a cada uno su número global (por ejemplo, `AC-09 (FR-07; FEAT-002 AC-10)` en FEAT-002a), el mismo criterio
de FEAT-001 (decisión del 2026-10-30).

FEAT-002a queda sobre 7 AC (11) por decisión del usuario del 2026-11-04: la fórmula del precio de venta
tiene que estar completa (Responsable Inscripto, Monotributo y redondeo al múltiplo) para que cualquier
alta grabe un precio correcto, y las validaciones del alta no pueden faltar al cargar; los seis AC de
cálculo son ejemplos de una única fórmula.

## Suggested implementation order
a → b → c (b depende de a; c depende de a, y de b para el criterio de volumen)

## Original context
`prd-DISC-001-02.md` cubre el catálogo de artículos: alta y edición con validaciones, el cálculo del
precio de venta según la condición fiscal con redondeo al múltiplo comercial, el recálculo del catálogo al
cambiar la configuración, la búsqueda y la baja lógica; 19 FR, 3 NFR y 22 AC. Al validarlo como FEAT-002
superó el umbral de alcance (W-PRD-06) y el usuario aprobó dividirlo en tres partes que se pueden poner en
producción por separado.
