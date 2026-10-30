# PRD DISC-001-02: Catálogo de artículos y precio de venta

| Campo | Valor |
|-------|-------|
| Ticket | DISC-001 (PRD 02 de 9) |
| Tracker | ninguno |
| Fecha | 2026-10-29 |
| PRD de origen | `PRD.md` (PRD-001) |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de PRD-001
> del que proviene. Los que surgen de decisiones de las dueñas tomadas en DISC-001 lo indican así.

## Context and Problem
Hoy los presupuestos se arman buscando precios en el folleto del proveedor, que trae precios de
costo con IVA incluido. El precio de venta se calcula a mano sumando el margen de utilidad, lo que
genera errores de producto y de precio.

El sistema necesita un catálogo de artículos que guarde el precio de costo y el margen, y que
calcule el precio de venta por sí solo. El cálculo depende de la condición fiscal de la óptica:
como Responsable Inscripto se descuenta el IVA del costo antes de aplicar el margen y se vuelve a
sumar después; como Monotributo el margen se aplica directo sobre el costo. El resultado se
redondea hacia arriba a un múltiplo comercial, para que el margen cargado funcione como piso.

## Goals
- Que el precio de venta de cada artículo salga siempre del costo, el margen y la configuración
  vigente, sin cálculo manual.
- Que un cambio de costo, de margen o de configuración actualice los precios de venta sin editar
  cada artículo.
- Que cada artículo se pueda identificar sin ambigüedad por su código en el proveedor, para que la
  actualización por planilla (PRD 03) sepa qué artículo tocar.

## Functional Requirements

### Alta y edición
- FR-01: El sistema debe permitir cargar un artículo con Código en el proveedor, Descripción,
  Precio de Costo (precio final del folleto del proveedor, con IVA incluido) y Margen de Utilidad
  (%), y asignarle un Código autonumérico al grabarlo. [PRD-001 RF-20]
- FR-02: El sistema debe exigir un Código en el proveedor y no debe admitir dos artículos con el
  mismo código, comparándolos sin distinguir mayúsculas de minúsculas e ignorando los espacios al
  principio y al final. [PRD-001 RF-94; decisión de las dueñas: único y obligatorio]
- FR-03: El sistema debe exigir la Descripción del artículo para grabarlo. [PRD-001 RF-20]
- FR-04: El sistema debe admitir un Margen de Utilidad entre 0 y 1.000 %, ambos inclusive.
  [PRD-001 RF-21; decisión de las dueñas: se amplía el tope de 100 % a 1.000 %]
- FR-05: El sistema debe admitir un Precio de Costo igual a 0 o negativo cargado a mano, y mostrar
  al grabarlo la advertencia "Precio de costo negativo o cero". [PRD-001 RF-80; decisión de las
  dueñas: mismo criterio que la planilla]
- FR-06: El sistema debe permitir modificar la Descripción, el Código en el proveedor, el Precio
  de Costo y el Margen de Utilidad de un artículo existente, con las mismas validaciones del alta.
  [PRD-001 RF-45]

### Cálculo del precio de venta
- FR-07: Cuando la condición fiscal configurada sea Responsable Inscripto, el sistema debe
  calcular el precio de venta en tres pasos: (a) Costo sin IVA = Precio de Costo / (1 + alícuota
  / 100); (b) Precio de Venta sin IVA = Costo sin IVA × (1 + Margen / 100); (c) Precio de Venta =
  Precio de Venta sin IVA × (1 + alícuota / 100), sin redondear los pasos (a) y (b).
  [PRD-001 RF-21]
- FR-08: Cuando la condición fiscal configurada sea Monotributo, el sistema debe calcular el precio
  de venta como Precio de Costo × (1 + Margen / 100), sin descontar IVA del costo.
  [PRD-001 RF-84]
- FR-09: El sistema debe tomar la alícuota de IVA del parámetro de configuración del PRD 01.
  [PRD-001 RF-46]
- FR-10: El sistema debe redondear el precio de venta calculado hacia arriba, al múltiplo de
  redondeo comercial configurado en el PRD 01, de modo que nunca quede por debajo del valor
  calculado. [PRD-001 RF-56, RF-57]
- FR-11: El sistema debe guardar el precio de venta ya redondeado según FR-10. [PRD-001 RF-58]
- FR-12: El sistema debe calcular el precio de venta sin que la operadora lo cargue y mostrarlo
  como campo de solo lectura. [PRD-001 RF-69, RF-70]
- FR-13: El sistema debe recalcular y guardar el precio de venta de un artículo cuando se grabe un
  cambio en su Precio de Costo o en su Margen de Utilidad. [PRD-001 RF-45]
- FR-14: El sistema debe recalcular y guardar el precio de venta de todos los artículos del
  catálogo, activos e inactivos, cuando se grabe un cambio en la alícuota de IVA, la condición
  fiscal o el múltiplo de redondeo comercial. [PRD-001 RF-86]
- FR-15: El sistema debe grabar el cambio de configuración de FR-14 y el recálculo de los precios
  como una sola operación: si el recálculo no se completa, no debe quedar grabado ni el parámetro
  nuevo ni ningún precio recalculado. [Derivado de PRD-001 RF-86: evita precios calculados con
  parámetros distintos]

### Consulta y baja
- FR-16: El sistema debe permitir listar los artículos y buscarlos por Código, Código en el
  proveedor o Descripción, por coincidencia parcial y sin distinguir mayúsculas ni acentos.
  [Derivado de PRD-001 RF-11 y AC-81: para editar un artículo hay que poder encontrarlo]
- FR-17: El sistema debe permitir desactivar un artículo sin eliminarlo ni modificar sus datos.
  [Decisión de las dueñas]
- FR-18: El sistema debe permitir reactivar un artículo desactivado. [Decisión de las dueñas]
- FR-19: El sistema debe indicar en el listado de FR-16 si cada artículo está activo o inactivo.
  [Decisión de las dueñas]

## Non-Functional Requirements
- NFR-01: El catálogo debe admitir hasta 10.000 artículos. [PRD-001 RNF-12]
- NFR-02: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge,
  en su versión estable vigente y en la inmediata anterior (4 combinaciones). [PRD-001 RNF-03]
- NFR-03: Los importes deben expresarse con 2 decimales. [PRD-001 RF-19]

## Acceptance Criteria
- AC-01 (FR-01, FR-12): WHEN la operadora carga un artículo nuevo con Código en el proveedor,
  Descripción, Precio de Costo y Margen de Utilidad y graba, THE sistema SHALL asignarle un Código
  autonumérico y guardarlo con su Precio de Venta calculado. [PRD-001 AC-30]
- AC-02 (FR-02): IF existe un artículo con Código en el proveedor "ABC-1" y la operadora graba otro
  con " abc-1 ", THEN THE sistema SHALL no grabarlo e indicar junto al campo que ese código ya
  pertenece a otro artículo.
- AC-03 (FR-02, FR-03): IF la operadora graba un artículo sin Código en el proveedor o sin
  Descripción, THEN THE sistema SHALL no grabarlo e indicar junto al campo faltante que debe
  completarlo.
- AC-04 (FR-04, FR-07): WHEN, con Responsable Inscripto, alícuota 21 y múltiplo 0,01, la operadora
  graba un artículo con Precio de Costo $1.210 y Margen 150 %, THE sistema SHALL guardar un Precio
  de Venta de $3.025,00.
- AC-05 (FR-04): IF la operadora ingresa un Margen de Utilidad de -1 o de 1.000,01, THEN THE sistema
  SHALL no grabar el artículo e indicar junto al campo que el margen debe estar entre 0 y 1.000.
- AC-06 (FR-05, FR-07): WHEN, con Responsable Inscripto, alícuota 21 y múltiplo 0,01, la operadora
  graba un artículo con Precio de Costo -$100 y Margen 50 %, THE sistema SHALL grabarlo con Precio
  de Venta -$150,00 y mostrar la advertencia "Precio de costo negativo o cero".
- AC-07 (FR-07, FR-09): WHEN, con Responsable Inscripto, alícuota 21 y múltiplo 0,01, la operadora
  graba un artículo con Precio de Costo $1.210 y Margen 50 %, THE sistema SHALL calcular un costo
  sin IVA de $1.000,00, un precio de venta sin IVA de $1.500,00 y guardar un Precio de Venta de
  $1.815,00. [PRD-001 AC-36]
- AC-08 (FR-07): WHEN, con Responsable Inscripto, alícuota 21 y múltiplo 0,01, la operadora graba
  un artículo con Precio de Costo $1.000 y Margen 50 %, THE sistema SHALL guardar un Precio de
  Venta de $1.500,00. [PRD-001 AC-13]
- AC-09 (FR-09, FR-14): WHEN la operadora cambia la alícuota de 21 a 10,5 con múltiplo 0,01 y luego
  graba un artículo con Precio de Costo $1.105 y Margen 50 %, THE sistema SHALL guardar un Precio
  de Venta de $1.657,50. [PRD-001 AC-72]
- AC-10 (FR-08): WHEN, con Monotributo y múltiplo 0,01, la operadora graba un artículo con Precio
  de Costo $1.210 y Margen 50 %, THE sistema SHALL guardar un Precio de Venta de $1.815,00.
  [PRD-001 AC-83]
- AC-11 (FR-10, FR-11): WHEN, con múltiplo $50, se graba un artículo cuyo precio de venta
  calculado es $1.815,37, THE sistema SHALL guardar un Precio de Venta de $1.850,00.
  [PRD-001 AC-38, AC-74]
- AC-12 (FR-10, FR-11, NFR-03): WHEN, con múltiplo $0,01, se graba un artículo cuyo precio de venta
  calculado es $1.666,656, THE sistema SHALL guardar un Precio de Venta de $1.666,66.
  [PRD-001 AC-39]
- AC-13 (FR-12): WHEN la operadora abre un artículo grabado para editarlo, THE sistema SHALL
  mostrar el Precio de Venta sin permitir modificar su valor. [PRD-001 AC-81]
- AC-14 (FR-06, FR-13): WHEN, con alícuota 21 y múltiplo 0,01, la operadora cambia de 50 % a 60 %
  el margen de un artículo con Precio de Costo $1.210 y graba, THE sistema SHALL guardar un Precio
  de Venta de $1.936,00. [PRD-001 AC-70]
- AC-15 (FR-06, FR-13): WHEN, con alícuota 21 y múltiplo 0,01, la operadora cambia a mano de $1.210
  a $2.420 el Precio de Costo de un artículo con Margen 50 % y graba, THE sistema SHALL guardar un
  Precio de Venta de $3.630,00. [PRD-001 AC-71]
- AC-16 (FR-14): WHEN la operadora cambia el múltiplo de $0,01 a $50 en la configuración y graba,
  THE sistema SHALL pasar de $1.815,00 a $1.850,00 el Precio de Venta guardado de un artículo con
  Precio de Costo $1.210 y Margen 50 %, sin que se edite el artículo. [PRD-001 AC-85]
- AC-17 (FR-14): WHEN la operadora graba un cambio de configuración que dispara el recálculo, THE
  sistema SHALL recalcular también el Precio de Venta de los artículos inactivos.
- AC-18 (FR-15): IF el recálculo de FR-14 no se completa, THEN THE sistema SHALL conservar el valor
  anterior del parámetro y los Precios de Venta anteriores de todos los artículos, e informar a la
  operadora que el cambio no se grabó.
- AC-19 (FR-16): WHEN la operadora busca "armazón" en el catálogo, THE sistema SHALL listar todos
  los artículos cuya Descripción contiene "Armazón" o "armazon", y ninguno que no lo contenga.
- AC-20 (FR-17, FR-19): WHEN la operadora desactiva un artículo, THE sistema SHALL mostrarlo como
  inactivo en el listado y conservar sin cambios su Código, Descripción, Precio de Costo, Margen y
  Precio de Venta.
- AC-21 (FR-18, FR-19): WHEN la operadora reactiva un artículo inactivo, THE sistema SHALL
  mostrarlo como activo en el listado.
- AC-22 (NFR-01): WHEN el catálogo tiene 10.000 artículos, THE sistema SHALL permitir grabar,
  modificar, buscar y desactivar artículos con los mismos resultados que en AC-01 a AC-21.

## Out of Scope
- Actualización de costos por planilla Excel: corresponde al PRD 03.
- Selección de artículos en las líneas del presupuesto y exclusión de los inactivos en esa
  búsqueda: corresponde al PRD 04.
- Eliminación definitiva de artículos (decisión de las dueñas: solo se desactivan).
- Control de stock o existencias. [PRD-001 Fuera de Alcance]
- Carga manual del precio de venta: siempre se calcula.
- Historial de precios de un artículo.

## Risks and Mitigations
- Riesgo: un costo ≤ 0 cargado a mano produce un precio de venta ≤ 0. Mitigación: advertencia al
  grabar (FR-05); el PRD 04 impide usar en un presupuesto un precio unitario negativo
  (PRD-001 RF-17).
- Riesgo: un margen mal tipeado (por ejemplo, 500 en lugar de 50) da un precio desproporcionado.
  Mitigación: tope de 1.000 % (FR-04) y precio de venta visible al grabar (FR-12).
- Riesgo: el recálculo de 10.000 artículos al cambiar la configuración tarda o falla a mitad de
  camino. Mitigación: operación única de FR-15. PRD-001 no fija un tiempo máximo para este
  recálculo; queda a definir en la spec.
- Riesgo: el recálculo cambia precios de presupuestos ya grabados. Mitigación: PRD-001 RF-87 lo
  prohíbe y se cubre en el PRD 04.

## Dependencies
- PRD 01 (DISC-001): acceso con sesión iniciada, parámetros de alícuota de IVA, condición fiscal y
  múltiplo de redondeo comercial, y mensajes de validación junto al campo (FR-16 del PRD 01).
- PRD 03 depende de este: usa el Código en el proveedor único (FR-02) y el recálculo de FR-13.
  Queda para el PRD 03 decidir si la planilla actualiza artículos inactivos.
- PRD 04 depende de este: toma el Precio de Venta guardado como precio unitario de la línea.
