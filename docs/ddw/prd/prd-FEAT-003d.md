# PRD FEAT-003d: Concurrencia, volumen y navegadores

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-003d (parte d de FEAT-003) |
| Tracker | ninguno |
| Fecha | 2026-11-06 |
| PRD padre | `docs/ddw/prd/prd-FEAT-003.md` (índice), derivado de `prd-DISC-001-04.md` |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de origen. Los AC
> se numeran desde AC-01 en este documento y cada uno cita entre paréntesis su número global en FEAT-003,
> que es el que usa el índice y el mismo que en `prd-DISC-001-04.md`.

## Context and Problem
FEAT-003a, FEAT-003b y FEAT-003c cubren el alta, los cálculos y el cierre de los presupuestos para una
sesión y una base chica. En el local trabajan dos sesiones a la vez sobre una base SQLite, que no admite
escrituras concurrentes como una base cliente-servidor, y los presupuestos se acumulan con los años. Esta
parte fija que dos sesiones puedan grabar presupuestos al mismo tiempo sin errores de bloqueo ni números
repetidos, que el sistema funcione igual con 10.000 presupuestos y que todos los criterios de FEAT-003 se
cumplan en Chrome y en Edge.

## Goals
- Que dos sesiones graben presupuestos a la vez con números distintos y consecutivos.
- Que el sistema se comporte igual con 10.000 presupuestos grabados.
- Que todo FEAT-003 funcione en los navegadores que usa el local.

## Functional Requirements
- FR-01: El sistema debe asignar a cada presupuesto, al grabarlo por primera vez, un número igual al
  último número asignado más 1, y el número 1 al primer presupuesto. [FEAT-003 FR-06; PRD-001 RF-06]

## Non-Functional Requirements
- NFR-01: El sistema debe admitir 2 sesiones grabando presupuestos al mismo tiempo sin que ninguna
  reciba un error de bloqueo de la base de datos. [FEAT-003 NFR-01; PRD-001 RNF-13]
- NFR-02: Todos los criterios de aceptación de FEAT-003a, FEAT-003b, FEAT-003c y de este documento deben
  cumplirse en Chrome y en Edge, en su versión estable vigente y en la inmediata anterior
  (4 combinaciones). [FEAT-003 NFR-02; PRD-001 RNF-03]
- NFR-03: El sistema debe admitir hasta 10.000 presupuestos grabados. [FEAT-003 NFR-04; PRD-001 RNF-01]

## Acceptance Criteria
- AC-01 (NFR-01, FR-01; FEAT-003 AC-36): WHEN dos sesiones graban un presupuesto nuevo al mismo tiempo,
  THE sistema SHALL grabar los dos con números distintos y consecutivos, sin que ninguna sesión reciba un
  error de bloqueo de la base. [PRD-001 AC-63]
- AC-02 (NFR-02; FEAT-003 AC-37): WHEN se ejecutan los criterios de FEAT-003a, FEAT-003b, FEAT-003c y de
  este documento en Chrome y en Edge, en su versión estable vigente y en la inmediata anterior, THE
  sistema SHALL cumplirlos en las 4 combinaciones. [PRD-001 AC-56]
- AC-03 (NFR-03; FEAT-003 AC-38): WHEN la base tiene 10.000 presupuestos grabados, THE sistema SHALL
  permitir grabar uno nuevo, modificar uno en Borrador y pasar uno a Final con los mismos resultados que
  en los AC de FEAT-003a, FEAT-003b, FEAT-003c y en AC-01 de este documento.

## Out of Scope
- Alta del presupuesto, datos del cliente y sus validaciones, numeración, estado inicial,
  búsqueda y selección de artículos y Precio Unitario de solo lectura: corresponden a FEAT-003a.
- Carga y validación de la Cantidad y del Porcentaje de Descuento de la línea, cálculo del Precio con
  Descuento, del Precio Final y del total, recálculo en pantalla e importes con IVA incluido:
  corresponden a FEAT-003b.
- Fecha del presupuesto no modificable, modificación de un presupuesto en Borrador, paso a Final con confirmación, rechazo de cambios sobre un
  presupuesto en Final y conservación de las líneas ante cambios del catálogo: corresponden a FEAT-003c.
- Dos sesiones que modifican el mismo presupuesto en Borrador a la vez: el riesgo se registra en
  FEAT-003c y se resuelve en su spec.
- Búsqueda de presupuestos y PDF del presupuesto: corresponden al PRD 05.
- Emisión de la factura desde el presupuesto Final: corresponde al PRD 06.
- Eliminación de presupuestos, incluidos los que están en Borrador. [PRD-001 Fuera de Alcance]
- Módulo de clientes: los datos del cliente se cargan en cada presupuesto y no se reutilizan entre
  presupuestos. [PRD-001 Fuera de Alcance]
- Líneas sin artículo del catálogo y modificación del Precio Unitario de la línea (decisión de las
  dueñas: precio fijo del catálogo, con descuento por línea).
- Descuento global sobre el total del presupuesto.
- Copiar o duplicar un presupuesto existente.
- Control de stock, cobros, señas y medios de pago. [PRD-001 Fuera de Alcance]

## Risks and Mitigations
- Riesgo: dos grabaciones simultáneas reciben el mismo número o una falla por bloqueo de SQLite.
  Mitigación: AC-01 exige números distintos y consecutivos sin error de bloqueo; la forma de lograrlo se
  decide en la spec.
- Riesgo: con 10.000 presupuestos la asignación del número o la grabación cambian de resultado.
  Mitigación: AC-03 repite con ese volumen los criterios de FEAT-003a, FEAT-003b, FEAT-003c y AC-01.
- Riesgo: un criterio que pasa en un navegador falla en otro. Mitigación: AC-02 los ejecuta en las 4
  combinaciones de Chrome y Edge.

## Dependencies
- FEAT-003a: alta y numeración de los presupuestos (FEAT-003 FR-06).
- FEAT-003b: cantidad, descuento y cálculos de las líneas y el total.
- FEAT-003c: fecha del presupuesto, edición en Borrador y paso a Final, que AC-03 ejercita con 10.000
  presupuestos.
- FEAT-001a: sesión iniciada (mergeado, PR #2).
