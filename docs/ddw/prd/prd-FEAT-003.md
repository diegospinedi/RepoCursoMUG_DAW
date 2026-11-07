# Parent PRD: Presupuestos: alta, líneas y cálculos

| Metric | Value |
|--------|-------|
| Ticket | FEAT-003 |
| Date | 2026-11-06 |
| Status | Split |
| Acceptance criteria to cover | 38 |

## Sub-tickets

| Sub-ticket | Title | PRD | ACs | Dependencies | Status |
|---|---|---|---|---|---|
| FEAT-003a | Alta del presupuesto con cliente y líneas del catálogo | prd-FEAT-003a.md | AC-01, AC-02, AC-03, AC-04, AC-05, AC-06, AC-07, AC-08, AC-09, AC-10, AC-12, AC-13, AC-21, AC-22, AC-23, AC-24, AC-29 | none | active |
| FEAT-003b | Cantidad, descuento y cálculos de las líneas y el total | prd-FEAT-003b.md | AC-25, AC-26, AC-27, AC-28, AC-30, AC-31, AC-32, AC-33 | depends on a | pending |
| FEAT-003c | Edición en Borrador y paso a Final irreversible | prd-FEAT-003c.md | AC-11, AC-14, AC-15, AC-16, AC-17, AC-18, AC-19, AC-20, AC-34, AC-35 | depends on a and b | pending |
| FEAT-003d | Concurrencia, volumen y navegadores | prd-FEAT-003d.md | AC-36, AC-37, AC-38 | depends on a, b and c | pending |

AC-01 a AC-38 vienen de `prd-DISC-001-04.md` (PRD 04 de DISC-001). En DEFINE no se agregaron criterios
globales; FEAT-003a suma un criterio local sin número global (su AC-18), que valida la regla de líneas
con Cantidad 1 y Porcentaje de Descuento 0 decidida por el usuario el 2026-11-06.

Los AC de esta tabla son los números globales, iguales a los de `prd-DISC-001-04.md`. Cada sub-PRD
numera sus criterios desde AC-01 y cita junto a cada uno su número global (por ejemplo,
`AC-13 (FR-09; FEAT-003 AC-21)` en FEAT-003a), el mismo criterio de FEAT-001 y FEAT-002 (decisión del
2026-10-30).

Decisiones del usuario del 2026-11-06:
- Cantidad de AC por parte, por encima de 7 (W-PRD-06): el usuario aprobó explícitamente FEAT-003a con
  19 AC (17 globales y 2 locales), FEAT-003b con 8 AC y FEAT-003c con 10 AC: la opción de 4 partes que
  eligió mostraba esas cantidades, y las dos decisiones siguientes agregaron los AC locales de FEAT-003a
  y pasaron AC-11 a FEAT-003c.
- Fecha en FEAT-003a: el alta asigna y muestra la fecha del día (FEAT-003a FR-15, parte de FEAT-003
  FR-07); que no cambie al modificar el presupuesto (FEAT-003 AC-11) se verifica en FEAT-003c.
- Líneas en FEAT-003a: mientras no exista FEAT-003b, cada línea se graba con Cantidad 1 y Porcentaje de
  Descuento 0, y su Precio Final es igual al Precio Unitario (FEAT-003a FR-14). FEAT-003b reemplaza
  esos valores fijos (FEAT-003b FR-01).
- Fecha del presupuesto: AC-11 y FR-07 de `prd-DISC-001-04.md` pasan de FEAT-003a a FEAT-003c.
- Numeración: los presupuestos se numeran desde 1 (FR-06 de `prd-DISC-001-04.md`), sin continuar la de
  los presupuestos hechos a mano.

## Suggested implementation order
a → b → c → d (b depende de a; c depende de a y de b; d depende de a, b y c)

## Original context
`prd-DISC-001-04.md` cubre los presupuestos: el alta con los datos del cliente y sus validaciones, la
numeración y la fecha, las líneas tomadas del catálogo con su cantidad y su descuento, el cálculo del
Precio con Descuento, del Precio Final y del total, la edición en Borrador, el paso a Final irreversible
y la concurrencia y el volumen; 28 FR, 4 NFR y 38 AC. Al validarlo como FEAT-003 superó el umbral de
alcance (W-PRD-06) y el usuario aprobó el 2026-11-06 dividirlo en cuatro partes que se pueden poner en
producción por separado.
