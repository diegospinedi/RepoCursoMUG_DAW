# PRD DISC-001-05: Búsqueda y PDF de presupuestos

| Campo | Valor |
|-------|-------|
| Ticket | DISC-001 (PRD 05 de 9) |
| Tracker | ninguno |
| Fecha | 2026-10-30 |
| PRD de origen | `PRD.md` (PRD-001) |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de PRD-001
> del que proviene. Los que surgen de decisiones de las dueñas tomadas en DISC-001 lo indican así.

## Context and Problem
Hoy los presupuestos hechos a mano no quedan en ningún lugar donde se puedan volver a encontrar:
cuando un cliente vuelve o pide de nuevo su presupuesto, hay que rehacerlo. Además, el
presupuesto se entrega escrito a mano, sin la imagen de la óptica.

Con el PRD 04 los presupuestos quedan grabados. Este PRD agrega la búsqueda, para encontrarlos por
fecha y por datos del cliente, y el PDF del presupuesto en Final, para descargarlo en la PC y
entregarlo o adjuntarlo a mano en un email o un mensaje de WhatsApp.

## Goals
- Encontrar cualquier presupuesto grabado por fecha o por datos del cliente.
- Entregar al cliente un PDF con la imagen de la óptica que muestre exactamente lo grabado en el
  presupuesto Final.
- Que un presupuesto en Borrador no pueda salir como PDF, para que el cliente reciba solo
  presupuestos cerrados.

## Functional Requirements

### Búsqueda
- FR-01: El sistema debe permitir buscar presupuestos por fecha, Apellido, Nombre y DNI del
  cliente. [PRD-001 RF-03]
- FR-02: El sistema debe combinar con "Y" los filtros cargados y mostrar solo los presupuestos que
  cumplen todos. [PRD-001 RF-81]
- FR-03: El sistema debe buscar el Apellido y el Nombre por coincidencia parcial (el dato guardado
  contiene el texto ingresado), sin distinguir mayúsculas ni acentos. [PRD-001 RF-82]
- FR-04: El sistema debe buscar el DNI por coincidencia parcial, ignorando los puntos y guiones
  del texto ingresado. [PRD-001 RF-88]
- FR-05: El sistema debe filtrar por fecha con un rango Desde/Hasta, con ambos límites inclusive,
  admitiendo que se cargue uno solo de los dos. [PRD-001 RF-83]
- FR-06: El sistema no debe buscar cuando la fecha Desde sea posterior a la fecha Hasta.
  [Derivado de PRD-001 RF-83 y RF-35]
- FR-07: El sistema debe listar todos los presupuestos cuando se busque sin ningún filtro cargado.
  [Decisión de las dueñas]
- FR-08: El sistema debe mostrar los resultados ordenados por número de presupuesto de mayor a
  menor, en páginas de 50. [Decisión de las dueñas]
- FR-09: El sistema debe mostrar en cada resultado número, fecha, Apellido, Nombre, DNI, estado y
  total del presupuesto. [Decisión de las dueñas]
- FR-10: El sistema debe permitir abrir un presupuesto desde la lista de resultados.
  [Derivado de PRD-001 RF-03: el presupuesto se busca para volver a verlo]

### PDF del presupuesto
- FR-11: El sistema debe permitir generar el PDF de un presupuesto en estado Final y descargarlo en
  la PC. [PRD-001 RF-02, RF-36]
- FR-12: El sistema no debe generar ni permitir descargar el PDF de un presupuesto en Borrador,
  venga el pedido de la pantalla o de un pedido directo a la API. [PRD-001 RF-09]
- FR-13: El sistema debe incluir en el PDF el número y la fecha del presupuesto, y los datos del
  cliente que tenga cargados, entre Apellido, Nombre, DNI, Domicilio, Email y Nro. de Teléfono.
  [Decisión de las dueñas: PDF completo]
- FR-14: El sistema debe incluir en el PDF cada línea con Código de Artículo, Descripción, Precio
  Unitario, Cantidad, Porcentaje de Descuento, Precio con Descuento y Precio Final, y el total del
  presupuesto, con los mismos importes grabados. [Decisión de las dueñas: PDF completo;
  PRD-001 RF-12, RF-15]
- FR-15: El sistema debe incluir en el PDF la leyenda "Precios finales, IVA incluido", sin
  discriminar IVA en las líneas ni en el total. [PRD-001 RF-37, RF-42]
- FR-16: El sistema debe incluir en el PDF el logo de la óptica y aplicar su color primario sobre
  su color de fondo, tal como los define la marca del PRD 01, sin otros datos de la óptica en el
  encabezado. [PRD-001 RF-55; decisión de las dueñas: solo logo y colores]

## Non-Functional Requirements
- NFR-01: La búsqueda de presupuestos debe mostrar los resultados en menos de 2 segundos en el
  percentil 95, con hasta 10.000 presupuestos grabados, medido desde que la operadora presiona
  Buscar hasta que la grilla muestra los resultados en pantalla. [PRD-001 RNF-01]
- NFR-02: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge,
  en su versión estable vigente y en la inmediata anterior (4 combinaciones). [PRD-001 RNF-03]
- NFR-03: Los importes del PDF deben expresarse con 2 decimales. [PRD-001 RF-19]

## Acceptance Criteria
- AC-01 (FR-01, FR-03): WHEN hay presupuestos de clientes con Apellido "González" y "Gómez" y la
  operadora busca por Apellido "ONZALEZ", THE sistema SHALL mostrar solo el presupuesto de
  "González". [PRD-001 AC-03]
- AC-02 (FR-01, FR-03): WHEN la operadora busca por Nombre "maria" y hay presupuestos de "María
  José" y "Ana", THE sistema SHALL mostrar solo el de "María José".
- AC-03 (FR-01, FR-04): WHEN hay un presupuesto de un cliente con DNI 23456789 y la operadora busca
  por DNI "3456" o "23.456", THE sistema SHALL mostrar ese presupuesto. [PRD-001 AC-87]
- AC-04 (FR-02, FR-05): WHEN hay dos presupuestos de "González", uno del 10/03/2026 y otro del
  20/03/2026, y la operadora busca Apellido "González" con fecha Desde 15/03/2026 y sin fecha Hasta,
  THE sistema SHALL mostrar solo el del 20/03/2026. [PRD-001 AC-68]
- AC-05 (FR-05): WHEN hay presupuestos del 09/03/2026, 10/03/2026, 20/03/2026 y 21/03/2026 y la
  operadora busca con fecha Desde 10/03/2026 y Hasta 20/03/2026, THE sistema SHALL mostrar solo los
  del 10/03/2026 y del 20/03/2026. [PRD-001 AC-69]
- AC-06 (FR-05): WHEN la operadora busca solo con fecha Hasta 10/03/2026, THE sistema SHALL mostrar
  los presupuestos del 10/03/2026 y anteriores, y ninguno posterior.
- AC-07 (FR-06): IF la operadora busca con fecha Desde 20/03/2026 y Hasta 10/03/2026, THEN THE
  sistema SHALL no buscar e indicar junto al campo Desde que no puede ser posterior a Hasta.
- AC-08 (FR-07, FR-08): WHEN hay 120 presupuestos grabados, numerados del 1 al 120, y la operadora
  busca sin filtros, THE sistema SHALL mostrar en la primera página los presupuestos 120 a 71, y en
  la tercera los presupuestos 20 a 1.
- AC-09 (FR-09): WHEN la búsqueda devuelve un presupuesto, THE sistema SHALL mostrar en ese
  resultado su número, fecha, Apellido, Nombre, DNI, estado y total.
- AC-10 (FR-10): WHEN la operadora abre un presupuesto desde la lista de resultados, THE sistema
  SHALL mostrar ese presupuesto con sus datos y líneas.
- AC-11 (FR-11, FR-16): WHEN la operadora presiona Descargar PDF en un presupuesto en Final, THE
  sistema SHALL descargar en la PC un PDF con el logo de la óptica y el color primario #0903A0
  sobre fondo #FFFFFF. [PRD-001 AC-02, AC-09, AC-33]
- AC-12 (FR-12): IF la operadora abre un presupuesto en Borrador o pide su PDF directamente a la
  API, THEN THE sistema SHALL mostrar deshabilitado el botón Descargar PDF y rechazar el pedido sin
  generar el PDF. [PRD-001 AC-08]
- AC-13 (FR-13): WHEN la operadora descarga el PDF del presupuesto 155 del 10/03/2026, con
  Apellido, Nombre, DNI y Teléfono cargados y sin Domicilio ni Email, THE sistema SHALL mostrar en
  el PDF el número 155, la fecha 10/03/2026 y esos cuatro datos, sin campos de Domicilio ni Email.
- AC-14 (FR-14, NFR-03): WHEN la operadora descarga el PDF de un presupuesto en Final con una línea
  de Precio Unitario $6,67, Cantidad 3, Descuento 50 %, Precio con Descuento $3,34 y Precio Final
  $10,02, y otra de Precio Final $2.700,00, THE sistema SHALL mostrar en el PDF esas dos líneas con
  sus siete campos y un total de $2.710,02.
- AC-15 (FR-15): WHEN la operadora descarga el PDF de un presupuesto en Final, THE sistema SHALL
  mostrar en el documento la leyenda "Precios finales, IVA incluido" y ningún importe de IVA
  discriminado. [PRD-001 AC-37]
- AC-16 (NFR-01): WHEN la base tiene 10.000 presupuestos y la operadora ejecuta 20 búsquedas por
  Apellido, THE sistema SHALL mostrar la grilla de resultados en menos de 2 segundos desde la
  pulsación de Buscar en al menos 19 de ellas. [PRD-001 AC-51]
- AC-17 (NFR-02): WHEN se ejecutan los criterios de este documento en Chrome y en Edge, en su
  versión estable vigente y en la inmediata anterior, THE sistema SHALL cumplirlos en las 4
  combinaciones. [PRD-001 AC-56]

## Out of Scope
- Búsqueda por número de presupuesto y filtro por estado (decisión de las dueñas: solo los filtros
  de PRD-001; el estado se ve en cada resultado).
- Datos de la óptica en el encabezado del PDF (nombre, domicilio, teléfono): solo logo y colores.
- Leyenda de validez o vencimiento del presupuesto.
- Envío automático del PDF por email o WhatsApp: la operadora lo descarga y lo adjunta a mano.
  [PRD-001 Fuera de Alcance]
- Impresión directa desde el sistema: se imprime desde el PDF descargado. [PRD-001 Fuera de
  Alcance]
- Historial de presupuestos por cliente. [PRD-001 Fuera de Alcance]
- Búsqueda y PDF de facturas: corresponden al PRD 08.

## Risks and Mitigations
- Riesgo: el PDF contiene datos personales del cliente (DNI, domicilio, email, teléfono) y, una
  vez descargado, queda fuera de la protección del sistema. Mitigación: solo se genera con sesión
  iniciada (PRD 01) y solo incluye los datos que el cliente dio; el resguardo del archivo en la PC
  queda a cargo de la operadora.
- Riesgo: la búsqueda sin filtros sobre 10.000 presupuestos excede los 2 segundos. Mitigación:
  páginas de 50 (FR-08) y medición de NFR-01 sobre la base completa (AC-16).
- Riesgo: el PDF muestra importes distintos de los grabados. Mitigación: FR-14 exige los mismos
  importes del presupuesto, que en Final ya no cambian (PRD 04 FR-13).

## Dependencies
- PRD 01 (DISC-001): acceso con sesión iniciada, logo y colores de la marca (FR-15) y mensajes de
  validación junto al campo (FR-16).
- PRD 04 (DISC-001): presupuestos grabados con número, fecha, datos del cliente, líneas, total y
  estado; presupuestos en Final inmodificables (FR-13).
