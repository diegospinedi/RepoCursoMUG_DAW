# PRD FEAT-003c: Edición en Borrador y paso a Final irreversible

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-003c (parte c de FEAT-003) |
| Tracker | ninguno |
| Fecha | 2026-11-06 |
| PRD padre | `docs/ddw/prd/prd-FEAT-003.md` (índice), derivado de `prd-DISC-001-04.md` |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de origen. Los AC
> se numeran desde AC-01 en este documento y cada uno cita entre paréntesis su número global en FEAT-003,
> que es el que usa el índice y el mismo que en `prd-DISC-001-04.md`.

> **Alcance (W-PRD-06).** Esta parte tiene 10 AC, por encima de 5 a 7. El usuario lo aprobó
> explícitamente el 2026-11-06 al elegir la división y al pasar a esta parte la fecha del presupuesto
> (FEAT-003 FR-07 y AC-11): la fecha fija, la edición en Borrador, el paso a Final con confirmación y la
> protección del presupuesto Final, incluida la conservación de sus líneas ante cambios del catálogo,
> forman una sola regla de cierre.

## Context and Problem
Con FEAT-003a y FEAT-003b la operadora graba un presupuesto en Borrador con sus líneas y cálculos. Un
presupuesto se trabaja en Borrador y, cuando se cierra, pasa a Final: desde ese momento no se modifica
más, porque es la base de la factura (PRD 06). Esta parte fija la fecha del presupuesto al grabarlo por
primera vez, permite modificar los datos y las líneas mientras el presupuesto está en Borrador, pasarlo
a Final con una confirmación previa, rechazar todo cambio sobre un presupuesto en Final y conservar las
líneas grabadas aunque cambie el catálogo.

El presupuesto guarda datos personales del cliente (DNI, domicilio, email y teléfono), alcanzados por la
Ley 25.326 de Protección de Datos Personales.

## Goals
- Que todo presupuesto conserve como fecha la del día en que se grabó por primera vez.
- Que la operadora pueda corregir un presupuesto mientras está en Borrador.
- Que un presupuesto Final no pueda cambiar, para que la factura emitida desde él coincida con lo que se
  le entregó al cliente.
- Que los precios de un presupuesto grabado no cambien cuando se recalcula o se modifica el catálogo.

## Functional Requirements
- FR-01: El sistema debe asignar a cada presupuesto, al grabarlo por primera vez, la fecha del día como
  fecha del presupuesto, y no debe permitir modificarla. [FEAT-003 FR-07; decisión de las dueñas]
- FR-02: El sistema debe permitir, mientras el presupuesto esté en Borrador, modificar los datos del
  cliente y agregar, modificar y quitar líneas, con las mismas validaciones del alta de FEAT-003a y de
  las líneas de FEAT-003b. [FEAT-003 FR-10; PRD-001 RF-07]
- FR-03: El sistema debe permitir pasar un presupuesto de Borrador a Final.
  [FEAT-003 FR-11; PRD-001 RF-04, AC-05]
- FR-04: El sistema debe pedir confirmación antes de pasar un presupuesto a Final, avisando que después
  no podrá modificarse, y dejarlo en Borrador si la operadora cancela.
  [FEAT-003 FR-12; decisión de las dueñas: el paso a Final es irreversible]
- FR-05: El sistema debe rechazar toda modificación de los datos o de las líneas de un presupuesto en
  Final, venga de la pantalla o de un pedido directo a la API. [FEAT-003 FR-13; PRD-001 RF-08]
- FR-06: El sistema debe rechazar el cambio de un presupuesto de Final a Borrador.
  [FEAT-003 FR-14; PRD-001 RF-67]
- FR-07: El sistema no debe modificar las líneas de un presupuesto grabado cuando cambie el artículo en
  el catálogo: recálculo de precios, cambio de costo o margen, o desactivación.
  [FEAT-003 FR-28; PRD-001 RF-87; derivado de RF-73]

## Non-Functional Requirements
- NFR-01: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge, en su
  versión estable vigente y en la inmediata anterior (4 combinaciones). [FEAT-003 NFR-02; PRD-001 RNF-03]

## Acceptance Criteria
- AC-01 (FR-01; FEAT-003 AC-11): WHEN la operadora graba un presupuesto por primera vez el 10/03/2026 y
  lo modifica el 12/03/2026, THE sistema SHALL conservar 10/03/2026 como fecha del presupuesto y no
  permitir modificarla.
- AC-02 (FR-02; FEAT-003 AC-14): WHEN la operadora modifica el Domicilio de un presupuesto en Borrador y
  graba, THE sistema SHALL grabar el cambio, mostrar un mensaje de confirmación y mostrar el nuevo
  Domicilio al volver a abrirlo. [PRD-001 AC-07]
- AC-03 (FR-02; FEAT-003 AC-15): WHEN la operadora agrega una línea y quita otra en un presupuesto en
  Borrador y graba, THE sistema SHALL mostrar al volver a abrirlo la línea agregada y no la quitada.
  [PRD-001 AC-24]
- AC-04 (FR-03, FR-04; FEAT-003 AC-16): WHEN la operadora pasa a Final un presupuesto en Borrador y
  confirma, THE sistema SHALL grabarlo en estado Final. [PRD-001 AC-05]
- AC-05 (FR-04; FEAT-003 AC-17): WHEN la operadora pide pasar a Final un presupuesto en Borrador, THE
  sistema SHALL pedir confirmación avisando que después no podrá modificarse, antes de grabar el cambio
  de estado.
- AC-06 (FR-04; FEAT-003 AC-18): IF la operadora cancela la confirmación del paso a Final, THEN THE
  sistema SHALL dejar el presupuesto en Borrador.
- AC-07 (FR-05; FEAT-003 AC-19): IF la operadora intenta modificar cualquier dato o línea de un
  presupuesto en Final, desde la pantalla o con un pedido directo a la API, THEN THE sistema SHALL
  rechazar el cambio y dejar el presupuesto sin modificaciones. [PRD-001 AC-66]
- AC-08 (FR-06; FEAT-003 AC-20): IF la operadora intenta cambiar a Borrador un presupuesto en Final, THEN
  THE sistema SHALL rechazar el cambio y dejarlo en Final. [PRD-001 AC-67]
- AC-09 (FR-07; FEAT-003 AC-34): WHEN el catálogo se recalcula y un artículo pasa de $1.815,00 a
  $1.850,00, THE sistema SHALL conservar el Precio Unitario de $1.815,00 en la línea de un presupuesto ya
  grabado con ese artículo. [PRD-001 AC-86]
- AC-10 (FR-07; FEAT-003 AC-35): WHEN se desactiva el artículo de una línea de un presupuesto en
  Borrador, THE sistema SHALL mostrar esa línea con su Código, Descripción y Precio Unitario sin cambios
  al volver a abrir el presupuesto.

## Out of Scope
- Alta del presupuesto, datos del cliente y sus validaciones, numeración, estado inicial, búsqueda y
  selección de artículos y Precio Unitario de solo lectura: corresponden a FEAT-003a.
- Carga y validación de la Cantidad y del Porcentaje de Descuento de la línea, cálculo del Precio con
  Descuento, del Precio Final y del total, recálculo en pantalla e importes con IVA incluido:
  corresponden a FEAT-003b.
- Grabación simultánea desde 2 sesiones, volumen de 10.000 presupuestos y verificación global en los
  navegadores: corresponden a FEAT-003d.
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
- Riesgo: acceso no autorizado a datos personales de clientes (DNI, domicilio, email, teléfono),
  alcanzados por la Ley 25.326, al abrir o modificar un presupuesto. Mitigación: esta parte no se
  implementa sin el acceso de FEAT-001a; toda pantalla y operación de presupuestos exige sesión iniciada
  (PRD 01 FR-01 y NFR-05).
- Riesgo: un presupuesto pasa a Final por error y ya no se puede corregir. Mitigación: confirmación
  previa con aviso (FR-04); el error se corrige con un presupuesto nuevo.
- Riesgo: dos sesiones modifican el mismo presupuesto en Borrador a la vez y una pisa los cambios de la
  otra. PRD-001 no define este caso; queda a resolver en la spec (aviso de conflicto o último grabado
  gana).
- Riesgo: un pedido directo a la API modifica un presupuesto en Final salteando la pantalla. Mitigación:
  el rechazo de FR-05 aplica a la pantalla y a la API (AC-07).

## Dependencies
- FEAT-003a: el alta del presupuesto, sus validaciones y sus líneas.
- FEAT-003b: la cantidad, el descuento y los cálculos de las líneas, que la edición en Borrador vuelve a
  aplicar (FR-02).
- FEAT-001a: sesión iniciada y protección de las operaciones con datos de clientes (mergeado, PR #2).
- FEAT-002b y FEAT-002c: desactivación de artículos y recálculo del catálogo, que AC-09 y AC-10
  ejercitan (mergeados, PR #6 y PR #7).
- FEAT-003d depende de esta parte.
- PRD 06 depende de esta parte: factura únicamente presupuestos en Final.
