# PRD FEAT-002a: Alta de artículos con precio de venta calculado

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-002a (parte a de FEAT-002) |
| Tracker | ninguno |
| Fecha | 2026-11-04 |
| PRD padre | `docs/ddw/prd/prd-FEAT-002.md` (índice), derivado de `prd-DISC-001-02.md` |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de origen. Los AC
> se numeran desde AC-01 en este documento y cada uno cita entre paréntesis su número global en FEAT-002
> (por ejemplo, `FEAT-002 AC-07`), que es el que usa el índice; es el mismo criterio que FEAT-001.

> **Alcance (W-PRD-06).** Esta parte tiene 11 AC, por encima de 5 a 7. El usuario lo decidió el
> 2026-11-04 al aprobar la división: la fórmula del precio de venta (Responsable Inscripto, Monotributo y
> redondeo al múltiplo) tiene que estar completa para que cualquier alta grabe un precio correcto, y las
> validaciones del alta no pueden faltar al cargar.

## Context and Problem
Hoy los presupuestos se arman buscando precios en el folleto del proveedor, que trae precios de costo con
IVA incluido, y el precio de venta se calcula a mano sumando el margen. Esta parte permite cargar artículos
en un catálogo y que el sistema calcule y guarde el precio de venta según la condición fiscal, la alícuota
de IVA y el múltiplo de redondeo comercial configurados en FEAT-001c.

## Goals
- Que la operadora pueda cargar un artículo y ver su precio de venta calculado, sin cálculo manual.
- Que el precio calculado respete la condición fiscal y redondee hacia arriba al múltiplo comercial.
- Que cada artículo quede identificado sin ambigüedad por su código en el proveedor.

## Functional Requirements
- FR-01: El sistema debe permitir cargar un artículo con Código en el proveedor, Descripción, Precio de
  Costo (precio final del folleto del proveedor, con IVA incluido) y Margen de Utilidad (%), y asignarle
  un Código autonumérico al grabarlo. [FEAT-002 FR-01; PRD-001 RF-20]
- FR-02: El sistema debe exigir un Código en el proveedor y no debe admitir dos artículos con el mismo
  código, comparándolos sin distinguir mayúsculas de minúsculas e ignorando los espacios al principio y
  al final. [FEAT-002 FR-02; PRD-001 RF-94]
- FR-03: El sistema debe exigir la Descripción del artículo para grabarlo. [FEAT-002 FR-03]
- FR-04: El sistema debe admitir un Margen de Utilidad entre 0 y 1.000 %, ambos inclusive.
  [FEAT-002 FR-04; decisión de las dueñas]
- FR-05: El sistema debe admitir un Precio de Costo igual a 0 o negativo cargado a mano, y mostrar al
  grabarlo la advertencia "Precio de costo negativo o cero". [FEAT-002 FR-05; PRD-001 RF-80]
- FR-06: Cuando la condición fiscal configurada sea Responsable Inscripto, el sistema debe calcular el
  precio de venta en tres pasos: (a) Costo sin IVA = Precio de Costo / (1 + alícuota / 100); (b) Precio
  de Venta sin IVA = Costo sin IVA × (1 + Margen / 100); (c) Precio de Venta = Precio de Venta sin IVA ×
  (1 + alícuota / 100), sin redondear los pasos (a) y (b). [FEAT-002 FR-07; PRD-001 RF-21]
- FR-07: Cuando la condición fiscal configurada sea Monotributo, el sistema debe calcular el precio de
  venta como Precio de Costo × (1 + Margen / 100), sin descontar IVA del costo. [FEAT-002 FR-08]
- FR-08: El sistema debe tomar la alícuota de IVA del parámetro de configuración de FEAT-001c.
  [FEAT-002 FR-09; PRD-001 RF-46]
- FR-09: El sistema debe redondear el precio de venta calculado hacia arriba, al múltiplo de redondeo
  comercial configurado en FEAT-001c, de modo que nunca quede por debajo del valor calculado.
  [FEAT-002 FR-10; PRD-001 RF-56, RF-57]
- FR-10: El sistema debe guardar el precio de venta ya redondeado según FR-09. [FEAT-002 FR-11]
- FR-11: El sistema debe calcular el precio de venta sin que la operadora lo cargue y mostrarlo como
  campo de solo lectura. [FEAT-002 FR-12; PRD-001 RF-69, RF-70]

## Non-Functional Requirements
- NFR-01: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge, en su
  versión estable vigente y en la inmediata anterior (4 combinaciones). [FEAT-002 NFR-02]
- NFR-02: Los importes deben expresarse con 2 decimales. [FEAT-002 NFR-03; PRD-001 RF-19]

## Acceptance Criteria
- AC-01 (FR-01, FR-11; FEAT-002 AC-01): WHEN la operadora carga un artículo nuevo con Código en el
  proveedor, Descripción, Precio de Costo y Margen de Utilidad y graba, THE sistema SHALL asignarle un
  Código autonumérico y guardarlo con su Precio de Venta calculado.
- AC-02 (FR-02; FEAT-002 AC-02): IF existe un artículo con Código en el proveedor "ABC-1" y la operadora
  graba otro con " abc-1 ", THEN THE sistema SHALL no grabarlo e indicar junto al campo que ese código ya
  pertenece a otro artículo.
- AC-03 (FR-02, FR-03; FEAT-002 AC-03): IF la operadora graba un artículo sin Código en el proveedor o
  sin Descripción, THEN THE sistema SHALL no grabarlo e indicar junto al campo faltante que debe
  completarlo.
- AC-04 (FR-04, FR-06; FEAT-002 AC-04): WHEN, con Responsable Inscripto, alícuota 21 y múltiplo 0,01, la
  operadora graba un artículo con Precio de Costo $1.210 y Margen 150 %, THE sistema SHALL guardar un
  Precio de Venta de $3.025,00.
- AC-05 (FR-04; FEAT-002 AC-05): IF la operadora ingresa un Margen de Utilidad de -1 o de 1.000,01, THEN
  THE sistema SHALL no grabar el artículo e indicar junto al campo que el margen debe estar entre 0 y
  1.000.
- AC-06 (FR-05, FR-06; FEAT-002 AC-06): WHEN, con Responsable Inscripto, alícuota 21 y múltiplo 0,01, la
  operadora graba un artículo con Precio de Costo -$100 y Margen 50 %, THE sistema SHALL grabarlo con
  Precio de Venta -$150,00 y mostrar la advertencia "Precio de costo negativo o cero".
- AC-07 (FR-06, FR-08; FEAT-002 AC-07): WHEN, con Responsable Inscripto, alícuota 21 y múltiplo 0,01, la
  operadora graba un artículo con Precio de Costo $1.210 y Margen 50 %, THE sistema SHALL calcular un
  costo sin IVA de $1.000,00, un precio de venta sin IVA de $1.500,00 y guardar un Precio de Venta de
  $1.815,00. [PRD-001 AC-36]
- AC-08 (FR-06; FEAT-002 AC-08): WHEN, con Responsable Inscripto, alícuota 21 y múltiplo 0,01, la
  operadora graba un artículo con Precio de Costo $1.000 y Margen 50 %, THE sistema SHALL guardar un
  Precio de Venta de $1.500,00. [PRD-001 AC-13]
- AC-09 (FR-07; FEAT-002 AC-10): WHEN, con Monotributo y múltiplo 0,01, la operadora graba un artículo
  con Precio de Costo $1.210 y Margen 50 %, THE sistema SHALL guardar un Precio de Venta de $1.815,00.
  [PRD-001 AC-83]
- AC-10 (FR-09, FR-10; FEAT-002 AC-11): WHEN, con múltiplo $50, se graba un artículo cuyo precio de venta
  calculado es $1.815,37, THE sistema SHALL guardar un Precio de Venta de $1.850,00. [PRD-001 AC-38]
- AC-11 (FR-09, FR-10, NFR-02; FEAT-002 AC-12): WHEN, con múltiplo $0,01, se graba un artículo cuyo
  precio de venta calculado es $1.666,656, THE sistema SHALL guardar un Precio de Venta de $1.666,66.
  [PRD-001 AC-39]

## Out of Scope
- Búsqueda, edición, desactivación y reactivación de artículos: corresponden a FEAT-002b.
- Recálculo del catálogo al cambiar la configuración y el volumen de 10.000 artículos: corresponden a
  FEAT-002c.
- Actualización de costos por planilla (PRD 03) y uso de los artículos en presupuestos (PRD 04).
- Eliminación definitiva de artículos, control de stock, carga manual del precio de venta e historial de
  precios.

## Risks and Mitigations
- Riesgo: un costo ≤ 0 cargado a mano produce un precio de venta ≤ 0. Mitigación: advertencia al grabar
  (FR-05); el PRD 04 impide usar en un presupuesto un precio unitario negativo.
- Riesgo: un margen mal tipeado (500 en lugar de 50) da un precio desproporcionado. Mitigación: tope de
  1.000 % (FR-04) y precio de venta visible al grabar (FR-11).
- Riesgo: mientras FEAT-002c no esté, un cambio de configuración no recalcula los artículos ya grabados.
  Mitigación: el orden a → b → c deja esta ventana corta y previa a la carga de datos reales.

## Dependencies
- FEAT-001a (sesión iniciada, mergeado, PR #2) y FEAT-001c (alícuota de IVA, condición fiscal y múltiplo
  de redondeo comercial, mergeado, PR #4).
- FEAT-002b y FEAT-002c dependen de esta parte.
