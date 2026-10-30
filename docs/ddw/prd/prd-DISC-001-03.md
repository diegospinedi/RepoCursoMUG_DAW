# PRD DISC-001-03: Actualización de costos por planilla Excel

| Campo | Valor |
|-------|-------|
| Ticket | DISC-001 (PRD 03 de 9) |
| Tracker | ninguno |
| Fecha | 2026-10-29 |
| PRD de origen | `PRD.md` (PRD-001) |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de PRD-001
> del que proviene. Los que surgen de decisiones de las dueñas tomadas en DISC-001 lo indican así.

## Context and Problem
Los proveedores cambian sus precios con frecuencia y envían listas nuevas. Actualizar a mano el
precio de costo de cientos de artículos, uno por uno, es lento y propenso a errores. Las dueñas
necesitan cargar una planilla Excel con el código de cada artículo en el proveedor y su precio de
costo nuevo, y que el sistema actualice los costos y recalcule los precios de venta.

La planilla puede venir con errores: códigos que no existen, precios vacíos o con texto, códigos
repetidos o un formato distinto del esperado. El sistema tiene que aplicar lo que es válido,
informar lo que no pudo aplicar y por qué, y no tocar nada cuando el formato no es el esperado.

## Goals
- Que una lista de precios del proveedor se aplique al catálogo en una sola carga.
- Que la operadora sepa, al terminar, qué filas no se aplicaron y por qué, y qué filas se
  aplicaron con un precio que conviene revisar.
- Que una planilla con formato equivocado no modifique ningún artículo.

## Functional Requirements

### Formato de la planilla
- FR-01: El sistema debe permitir cargar una planilla Excel para actualizar los precios de costo de
  los artículos. [PRD-001 RF-22]
- FR-02: El sistema debe aceptar únicamente archivos en formato Excel .xlsx. [Decisión de las
  dueñas]
- FR-03: El sistema debe exigir que la planilla tenga exactamente dos columnas, con los encabezados
  "Codigo en el proveedor" y "Precio de Costo Nuevo" en la fila 1, en ese orden, comparados sin
  distinguir mayúsculas, acentos ni espacios al principio y al final. [PRD-001 RF-23; decisión de
  las dueñas sobre los encabezados]
- FR-04: El sistema debe rechazar completa, sin actualizar ningún artículo, una planilla que no
  cumpla FR-02 o FR-03 (archivo de otro formato, columnas faltantes, columnas de más o encabezados
  distintos). [PRD-001 RF-78]
- FR-05: El sistema debe informar a la operadora que el formato de la planilla no es el esperado
  cuando la rechace según FR-04, indicando el formato correcto. [PRD-001 RF-79]

### Aplicación de las filas
- FR-06: El sistema debe actualizar el precio de costo de cada artículo cuyo Código en el
  proveedor coincida con una fila de la planilla, y recalcular su precio de venta según el PRD 02.
  [PRD-001 RF-47]
- FR-07: El sistema debe comparar el Código en el proveedor sin distinguir mayúsculas de
  minúsculas e ignorando los espacios al principio y al final. [PRD-001 RF-94]
- FR-08: El sistema debe considerar inválido un Precio de Costo Nuevo vacío o no numérico.
  [PRD-001 RF-80]
- FR-09: El sistema debe aceptar un Precio de Costo Nuevo negativo o igual a cero, y actualizar el
  artículo con ese valor. [PRD-001 RF-80; decisión explícita registrada en PRD-001 Riesgos]
- FR-10: El sistema no debe aplicar ninguna de las filas cuyo código aparezca más de una vez en la
  misma planilla, comparando según FR-07. [Decisión de las dueñas]
- FR-11: El sistema debe actualizar también los artículos desactivados cuyo código coincida.
  [Decisión de las dueñas]
- FR-12: El sistema debe aplicar las filas válidas aunque otras filas de la misma planilla sean
  inválidas. [PRD-001 RF-24, AC-15]

### Resultado
- FR-13: El sistema debe mostrar en pantalla, al terminar, la lista de las filas que no pudo
  aplicar con la razón de cada una: "código inexistente", "precio inválido" o "código repetido en
  la planilla". [PRD-001 RF-24; decisión de las dueñas sobre el código repetido]
- FR-14: El sistema debe listar, junto con el resultado de FR-13, las filas que aplicó con un
  Precio de Costo Nuevo negativo o igual a cero, con la indicación "actualizado con precio negativo
  o cero". [PRD-001 RF-93]
- FR-15: El sistema debe indicar como "inactivo", en el resultado, cada artículo desactivado que
  actualizó según FR-11. [Decisión de las dueñas]

## Non-Functional Requirements
- NFR-01: Con un catálogo de 10.000 artículos, el sistema debe procesar una planilla de hasta
  10.000 filas en menos de 120 segundos, contados desde que la operadora confirma la carga hasta
  que se muestra el resultado. [PRD-001 RNF-12]
- NFR-02: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge,
  en su versión estable vigente y en la inmediata anterior (4 combinaciones). [PRD-001 RNF-03]

## Acceptance Criteria
- AC-01 (FR-01, FR-06): WHEN, con alícuota 21, múltiplo $0,01 y un artículo con Código en el
  proveedor "ABC-1", Precio de Costo $1.210 y Margen 50 %, la operadora carga una planilla con la
  fila "ABC-1" y precio $2.420, THE sistema SHALL pasar el Precio de Costo a $2.420,00 y el Precio
  de Venta a $3.630,00. [PRD-001 AC-14]
- AC-02 (FR-12, FR-13): WHEN la operadora carga una planilla con filas válidas y una fila con un
  código que no existe en el catálogo, THE sistema SHALL actualizar los artículos de las filas
  válidas y listar la fila inexistente con la razón "código inexistente". [PRD-001 AC-15]
- AC-03 (FR-08, FR-13): WHEN la operadora carga una planilla con una fila de precio vacío, otra con
  el precio "abc" y otras filas válidas, THE sistema SHALL actualizar las filas válidas y listar las
  dos mencionadas con la razón "precio inválido". [PRD-001 AC-75]
- AC-04 (FR-04, FR-05): IF la planilla tiene una tercera columna además de las dos del formato,
  THEN THE sistema SHALL no actualizar ningún artículo y mostrar un mensaje que indica que el
  formato de la planilla no es el esperado. [PRD-001 AC-76]
- AC-05 (FR-03, FR-04): IF la fila 1 de la planilla dice "Codigo" y "Precio", THEN THE sistema
  SHALL no actualizar ningún artículo y mostrar el mensaje de formato no esperado con los
  encabezados correctos.
- AC-06 (FR-03): WHEN la fila 1 de la planilla dice " CÓDIGO EN EL PROVEEDOR " y "precio de costo
  nuevo", THE sistema SHALL aceptar el formato y procesar las filas.
- AC-07 (FR-02, FR-05): IF la operadora carga un archivo .xls o .csv, THEN THE sistema SHALL no
  actualizar ningún artículo y mostrar un mensaje que pide guardar la planilla como .xlsx.
- AC-08 (FR-09, FR-14): WHEN la operadora carga una planilla con una fila de precio -100 y otra de
  precio 0 para artículos existentes, THE sistema SHALL actualizar ambos artículos y listarlos con
  la indicación "actualizado con precio negativo o cero". [PRD-001 AC-90]
- AC-09 (FR-07): WHEN existe un artículo con Código en el proveedor "ABC-1" y la operadora carga una
  planilla con la fila "abc-1" y precio $2.420, THE sistema SHALL actualizar su Precio de Costo a
  $2.420,00 y no listarlo como no actualizado. [PRD-001 AC-91]
- AC-10 (FR-07): WHEN existe un artículo con Código en el proveedor "ABC-1" y la operadora carga una
  planilla con la fila " ABC-1 " y precio $2.420, THE sistema SHALL actualizar su Precio de Costo a
  $2.420,00 y no listarlo como no actualizado. [PRD-001 AC-92]
- AC-11 (FR-10, FR-13): IF la planilla trae las filas "ABC-1" con $2.420 y "abc-1" con $2.500,
  THEN THE sistema SHALL dejar sin cambios el Precio de Costo de ese artículo y listar ambas filas
  con la razón "código repetido en la planilla".
- AC-12 (FR-11, FR-15): WHEN la planilla trae el código de un artículo desactivado con un precio
  válido, THE sistema SHALL actualizar su Precio de Costo y su Precio de Venta, mantenerlo
  desactivado e indicarlo como "inactivo" en el resultado.
- AC-13 (NFR-01): WHEN, con un catálogo de 10.000 artículos, la operadora carga una planilla de
  10.000 filas válidas, THE sistema SHALL actualizar los precios de costo, recalcular los de venta
  y mostrar el resultado en menos de 120 segundos. [PRD-001 AC-62]

## Out of Scope
- Alta de artículos nuevos desde la planilla: solo se actualizan artículos existentes.
- Actualización por planilla de la descripción, el margen u otro dato que no sea el precio de
  costo.
- Formatos distintos de .xlsx (.xls, .csv, PDF del folleto). [Decisión de las dueñas]
- Planillas con columnas en otro orden o con un formato propio de cada proveedor.
- Historial de las planillas cargadas.

## Risks and Mitigations
- Riesgo: un precio ≤ 0 aplicado por planilla produce un precio de venta ≤ 0. Mitigación: la fila
  se lista para revisión (FR-14) y el PRD 04 impide usar un precio unitario negativo en un
  presupuesto (PRD-001 RF-17). Aceptar estos valores fue decisión explícita del responsable del
  proyecto (PRD-001 Riesgos).
- Riesgo: un proveedor cambia los encabezados o agrega columnas. Mitigación: la planilla se rechaza
  completa con el formato correcto en el mensaje (FR-04, FR-05).
- Riesgo: el proceso se interrumpe a mitad de una planilla grande y deja parte aplicada sin
  resultado visible. PRD-001 no define este caso; queda a definir en la spec.
- Riesgo: un archivo .xlsx malicioso. Mitigación a cargo de la spec: el archivo solo se lee como
  datos y nunca se ejecuta su contenido.

## Dependencies
- PRD 01 (DISC-001): acceso con sesión iniciada y mensajes de validación.
- PRD 02 (DISC-001): Código en el proveedor único (FR-02 del PRD 02), cálculo y recálculo del
  precio de venta (FR-07 a FR-13 del PRD 02) y estado activo o inactivo de los artículos.
- PRD 04 (DISC-001): impide usar en un presupuesto un precio unitario negativo.
