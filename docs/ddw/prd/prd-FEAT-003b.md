# PRD FEAT-003b: Cantidad, descuento y cálculos de las líneas y el total

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-003b (parte b de FEAT-003) |
| Tracker | ninguno |
| Fecha | 2026-11-06 |
| PRD padre | `docs/ddw/prd/prd-FEAT-003.md` (índice), derivado de `prd-DISC-001-04.md` |
| PRD loops | 1 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de origen. Los AC
> se numeran desde AC-01 en este documento y cada uno cita entre paréntesis su número global en FEAT-003,
> que es el que usa el índice y el mismo que en `prd-DISC-001-04.md`.

## Context and Problem
Con el alta de FEAT-003a la operadora graba un presupuesto con líneas tomadas del catálogo, pero hoy los
descuentos, los precios de cada línea y el total se calculan a mano, con errores de cuenta. Esta parte
permite cargar la Cantidad y el Porcentaje de Descuento de cada línea y hace que el sistema calcule el
Precio con Descuento, el Precio Final y el total, con IVA incluido y con 2 decimales, recalculándolos en
pantalla al cambiar la cantidad o el descuento.

## Goals
- Que los descuentos, los precios de línea y el total se calculen solos y siempre cierren con lo que se
  muestra.
- Que la cantidad y el descuento de cada línea queden validados al cargarlos.
- Que los importes se muestren como precios finales al consumidor, con IVA incluido.

## Functional Requirements
- FR-01: El sistema debe permitir cargar y modificar la Cantidad y el Porcentaje de Descuento de la
  línea después de seleccionar el artículo. Estos valores reemplazan la Cantidad 1 y el Porcentaje de
  Descuento 0 fijos con los que FEAT-003a graba las líneas (FEAT-003a FR-14).
  [FEAT-003 FR-19; PRD-001 RF-40; decisión del usuario 2026-11-06]
- FR-02: El sistema debe admitir como Cantidad únicamente un número entero mayor a 0 y de hasta 9.999 unidades
  por línea. [FEAT-003 FR-20; PRD-001 RF-16, RF-60; tope decidido por el usuario el 2026-11-11]
- FR-03: El sistema debe admitir como Porcentaje de Descuento únicamente un valor entre 0 y 100, ambos
  inclusive. [FEAT-003 FR-21; PRD-001 RF-59]
- FR-04: El sistema debe calcular el Precio con Descuento de cada línea como Precio Unitario × (1 −
  Porcentaje de Descuento / 100), redondeado a 2 decimales con mitad hacia arriba, y mostrarlo en todas
  las líneas, incluidas las de descuento 0. [FEAT-003 FR-23; PRD-001 RF-13, RF-19, RF-41, RF-44]
- FR-05: El sistema debe calcular el Precio Final de cada línea como el Precio con Descuento ya
  redondeado por la Cantidad, redondeado a 2 decimales con mitad hacia arriba.
  [FEAT-003 FR-24; PRD-001 RF-14, RF-19, RF-44]
- FR-06: El sistema debe calcular el total del presupuesto como la suma de los Precios Finales ya
  redondeados de todas sus líneas. [FEAT-003 FR-25; PRD-001 RF-15]
- FR-07: El sistema debe recalcular en pantalla el Precio con Descuento, el Precio Final y el total
  cuando la operadora cambie la Cantidad o el Porcentaje de Descuento de una línea.
  [FEAT-003 FR-26; PRD-001 RF-40, AC-46]
- FR-08: El sistema debe mostrar todos los importes del presupuesto con IVA incluido, sin discriminar IVA
  en las líneas ni en el total. [FEAT-003 FR-27; PRD-001 RF-18, RF-42]

## Non-Functional Requirements
- NFR-01: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge, en su
  versión estable vigente y en la inmediata anterior (4 combinaciones). [FEAT-003 NFR-02; PRD-001 RNF-03]
- NFR-02: Los importes deben expresarse con 2 decimales, redondeados con mitad hacia arriba.
  [FEAT-003 NFR-03; PRD-001 RF-19]

## Acceptance Criteria
- AC-01 (FR-01, FR-07; FEAT-003 AC-25): WHEN la operadora cambia de 1 a 3 la Cantidad y de 0 a 10 el
  Porcentaje de Descuento de una línea con Precio Unitario $1.000,00, THE sistema SHALL mostrar un Precio
  con Descuento de $900,00 y un Precio Final de $2.700,00. [PRD-001 AC-27, AC-35, AC-46]
- AC-02 (FR-02; FEAT-003 AC-26): IF la operadora ingresa como Cantidad 0, -1 o 2,5, THEN THE sistema
  SHALL no aceptar el valor y mostrar junto al campo el mensaje "La cantidad debe ser un número entero
  mayor a 0". [PRD-001 AC-12, AC-43]
- AC-03 (FR-03; FEAT-003 AC-27): IF la operadora ingresa como Porcentaje de Descuento -1 o 100,01, THEN
  THE sistema SHALL no aceptar el valor y mostrar junto al campo el mensaje "El descuento debe estar entre
  0 y 100". [PRD-001 AC-42]
- AC-04 (FR-03, FR-04; FEAT-003 AC-28): WHEN la operadora ingresa un Porcentaje de Descuento de 100 en
  una línea con Precio Unitario $1.000,00, THE sistema SHALL aceptarlo y mostrar un Precio con Descuento
  de $0,00.
- AC-05 (FR-04; FEAT-003 AC-30): WHEN la operadora carga un Porcentaje de Descuento de 0 en una línea con
  Precio Unitario $1.000,00, THE sistema SHALL mostrar un Precio con Descuento de $1.000,00.
  [PRD-001 AC-26]
- AC-06 (FR-04, FR-05, NFR-02; FEAT-003 AC-31): WHEN la operadora carga Cantidad 3 y Porcentaje de
  Descuento 50 en una línea con Precio Unitario $6,67, THE sistema SHALL mostrar un Precio con Descuento
  de $3,34 y un Precio Final de $10,02. [PRD-001 AC-29]
- AC-07 (FR-06; FEAT-003 AC-32): WHEN la operadora agrega una línea con Precio Final $2.700,00 a un
  presupuesto cuya única línea tiene Precio Final $10,02 calculado como en AC-06, THE sistema SHALL
  mostrar un total de $2.710,02. [PRD-001 AC-11]
- AC-08 (FR-08; FEAT-003 AC-33): WHEN la operadora abre un presupuesto con líneas, THE sistema SHALL
  mostrar las líneas y el total sin discriminar IVA, con los importes finales al consumidor.
  [PRD-001 AC-28]
- AC-09 (FR-02): IF la operadora ingresa como Cantidad 10.000, THEN THE sistema SHALL no aceptar el valor y mostrar
  junto al campo el mensaje "Usá hasta 9.999 unidades.". [decisión del usuario 2026-11-11; sin número global en
  FEAT-003]

## Out of Scope
- Alta del presupuesto, datos del cliente y sus validaciones, numeración, estado inicial,
  búsqueda y selección de artículos y Precio Unitario de solo lectura: corresponden a FEAT-003a.
- Fecha del presupuesto no modificable, modificación de un presupuesto en Borrador, paso a Final con confirmación, rechazo de cambios sobre un
  presupuesto en Final y conservación de las líneas ante cambios del catálogo: corresponden a FEAT-003c.
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
- Riesgo: el total mostrado no coincide con la suma de las líneas por redondear en distinto orden.
  Mitigación: cada paso redondea a 2 decimales con mitad hacia arriba y el total suma los Precios Finales
  ya redondeados (FR-04 a FR-06, AC-06 y AC-07).
- Riesgo: el cálculo en pantalla y el que se graba difieren. Mitigación: los dos aplican las mismas
  fórmulas de FR-04 a FR-06, verificadas por los mismos ejemplos de AC-01 y AC-04 a AC-07.
- Riesgo: un descuento de 100 % deja una línea en $0,00 por error de carga. Mitigación: el Precio con
  Descuento y el Precio Final se recalculan en pantalla al cambiar el descuento (FR-07), a la vista de la
  operadora antes de grabar.

## Dependencies
- FEAT-003a: el presupuesto, sus líneas, la selección del artículo y el Precio Unitario tomado del
  catálogo.
- FEAT-001a: sesión iniciada y mensajes de validación junto al campo (mergeado, PR #2).
- FEAT-003c y FEAT-003d dependen de esta parte.
