# PRD DISC-001-04: Presupuestos: alta, líneas y cálculos

| Campo | Valor |
|-------|-------|
| Ticket | DISC-001 (PRD 04 de 9) |
| Tracker | ninguno |
| Fecha | 2026-10-30 |
| PRD de origen | `PRD.md` (PRD-001) |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de PRD-001
> del que proviene. Los que surgen de decisiones de las dueñas tomadas en DISC-001 lo indican así.

## Context and Problem
Hoy los presupuestos se escriben a mano: se copian los productos y los precios del folleto del
proveedor y se calculan a mano descuentos y totales. Eso genera errores de producto, de precio y
de cuenta, y los presupuestos no quedan guardados en ningún lugar donde se puedan volver a
encontrar.

El sistema necesita que la operadora arme el presupuesto eligiendo artículos del catálogo, con el
precio de venta ya calculado (PRD 02), que calcule por sí solo el descuento, el precio de cada
línea y el total, y que lo guarde con los datos del cliente. Un presupuesto se trabaja en
Borrador y, cuando se cierra, pasa a Final: desde ese momento no se modifica más, porque es la
base de la factura (PRD 06).

El presupuesto guarda datos personales del cliente (DNI, domicilio, email y teléfono), alcanzados
por la Ley 25.326 de Protección de Datos Personales.

## Goals
- Que todo presupuesto quede grabado con un número propio, la fecha y los datos del cliente.
- Que los productos y los precios de las líneas salgan del catálogo, sin transcribirlos a mano.
- Que los descuentos, los precios de línea y el total se calculen solos y siempre cierren con lo
  que se muestra.
- Que un presupuesto Final no pueda cambiar, para que la factura emitida desde él coincida con lo
  que se le entregó al cliente.

## Functional Requirements

### Alta y datos del cliente
- FR-01: El sistema debe permitir grabar un presupuesto nuevo con los datos del cliente Apellido,
  Nombre, DNI, Domicilio, Email y Nro. de Teléfono, guardados dentro del propio presupuesto.
  [PRD-001 RF-01, RF-05, RF-38]
- FR-02: El sistema debe exigir Apellido, Nombre y DNI del cliente para grabar un presupuesto.
  [PRD-001 RF-61]
- FR-03: El sistema debe permitir grabar un presupuesto sin Domicilio, Email ni Nro. de Teléfono
  del cliente. [PRD-001 RF-74]
- FR-04: El sistema debe admitir como DNI únicamente 7 u 8 dígitos, aceptando que se escriban con
  puntos y guardándolo sin ellos. [Decisión de las dueñas: el DNI se envía a ARCA en el PRD 06]
- FR-05: El sistema debe admitir en el Email, cuando se cargue, únicamente una dirección con el
  formato usuario@dominio.extensión. [Decisión de las dueñas]
- FR-06: El sistema debe asignar a cada presupuesto, al grabarlo por primera vez, un número igual
  al último número asignado más 1, y el número 1 al primer presupuesto. [PRD-001 RF-06]
- FR-07: El sistema debe asignar a cada presupuesto, al grabarlo por primera vez, la fecha del día
  como fecha del presupuesto, y no debe permitir modificarla. [Decisión de las dueñas]
- FR-08: El sistema debe exigir al menos una línea para grabar un presupuesto.
  [Decisión de las dueñas; PRD-001 AC-01, AC-04]

### Estados
- FR-09: El sistema debe manejar dos estados del presupuesto, Borrador y Final, y dejar en
  Borrador todo presupuesto grabado por primera vez. [PRD-001 RF-04, AC-04]
- FR-10: El sistema debe permitir, mientras el presupuesto esté en Borrador, modificar los datos
  del cliente y agregar, modificar y quitar líneas, con las mismas validaciones del alta.
  [PRD-001 RF-07]
- FR-11: El sistema debe permitir pasar un presupuesto de Borrador a Final. [PRD-001 RF-04, AC-05]
- FR-12: El sistema debe pedir confirmación antes de pasar un presupuesto a Final, avisando que
  después no podrá modificarse, y dejarlo en Borrador si la operadora cancela.
  [Decisión de las dueñas: el paso a Final es irreversible]
- FR-13: El sistema debe rechazar toda modificación de los datos o de las líneas de un presupuesto
  en Final, venga de la pantalla o de un pedido directo a la API. [PRD-001 RF-08]
- FR-14: El sistema debe rechazar el cambio de un presupuesto de Final a Borrador.
  [PRD-001 RF-67]

### Líneas
- FR-15: El sistema debe permitir buscar, al cargar una línea, los artículos activos del catálogo
  por Código o por Descripción, por coincidencia parcial y sin distinguir mayúsculas ni acentos, y
  no debe ofrecer los artículos inactivos. [PRD-001 RF-11; PRD 02 deja la exclusión de los
  inactivos para este PRD]
- FR-16: El sistema debe completar el Código, la Descripción y el Precio Unitario de la línea al
  seleccionarse un artículo, tomando como Precio Unitario el precio de venta guardado en el
  catálogo, sin volver a calcularlo. [PRD-001 RF-39, RF-68, RF-73]
- FR-17: El sistema debe mostrar el Precio Unitario de la línea como un campo de solo lectura.
  [Decisión de las dueñas: las líneas salen del catálogo con precio fijo]
- FR-18: El sistema no debe admitir en una línea un artículo cuyo precio de venta sea negativo.
  [PRD-001 RF-17]
- FR-19: El sistema debe permitir cargar y modificar la Cantidad y el Porcentaje de Descuento de
  la línea después de seleccionar el artículo. [PRD-001 RF-40]
- FR-20: El sistema debe admitir como Cantidad únicamente un número entero mayor a 0.
  [PRD-001 RF-16, RF-60]
- FR-21: El sistema debe admitir como Porcentaje de Descuento únicamente un valor entre 0 y 100,
  ambos inclusive. [PRD-001 RF-59]
- FR-22: El sistema debe registrar y mostrar en cada línea Código de Artículo, Descripción, Precio
  Unitario, Cantidad, Porcentaje de Descuento, Precio con Descuento y Precio Final.
  [PRD-001 RF-12]

### Cálculos
- FR-23: El sistema debe calcular el Precio con Descuento de cada línea como Precio Unitario × (1 −
  Porcentaje de Descuento / 100), redondeado a 2 decimales con mitad hacia arriba, y mostrarlo en
  todas las líneas, incluidas las de descuento 0. [PRD-001 RF-13, RF-19, RF-41, RF-44]
- FR-24: El sistema debe calcular el Precio Final de cada línea como el Precio con Descuento ya
  redondeado por la Cantidad, redondeado a 2 decimales con mitad hacia arriba.
  [PRD-001 RF-14, RF-19, RF-44]
- FR-25: El sistema debe calcular el total del presupuesto como la suma de los Precios Finales ya
  redondeados de todas sus líneas. [PRD-001 RF-15]
- FR-26: El sistema debe recalcular en pantalla el Precio con Descuento, el Precio Final y el
  total cuando la operadora cambie la Cantidad o el Porcentaje de Descuento de una línea.
  [PRD-001 RF-40, AC-46]
- FR-27: El sistema debe mostrar todos los importes del presupuesto con IVA incluido, sin
  discriminar IVA en las líneas ni en el total. [PRD-001 RF-18, RF-42]
- FR-28: El sistema no debe modificar las líneas de un presupuesto grabado cuando cambie el
  artículo en el catálogo: recálculo de precios, cambio de costo o margen, o desactivación.
  [PRD-001 RF-87; derivado de RF-73]

## Non-Functional Requirements
- NFR-01: El sistema debe admitir 2 sesiones grabando presupuestos al mismo tiempo sin que ninguna
  reciba un error de bloqueo de la base de datos. [PRD-001 RNF-13]
- NFR-02: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge,
  en su versión estable vigente y en la inmediata anterior (4 combinaciones). [PRD-001 RNF-03]
- NFR-03: Los importes deben expresarse con 2 decimales, redondeados con mitad hacia arriba.
  [PRD-001 RF-19]
- NFR-04: El sistema debe admitir hasta 10.000 presupuestos grabados. [PRD-001 RNF-01]

## Acceptance Criteria
- AC-01 (FR-01): WHEN la operadora carga Apellido, Nombre, DNI, Domicilio, Email y Nro. de
  Teléfono y una línea con un artículo y graba, THE sistema SHALL grabar el presupuesto, mostrar un
  mensaje que confirme que se grabó y mostrar esos seis datos al volver a abrirlo.
  [PRD-001 AC-01, AC-23]
- AC-02 (FR-02): IF la operadora graba un presupuesto con Apellido y Nombre y sin DNI, THEN THE
  sistema SHALL no grabarlo y mostrar junto al campo DNI el mensaje "Ingresá el DNI del cliente".
  [PRD-001 AC-34]
- AC-03 (FR-02): IF la operadora graba un presupuesto sin Apellido o sin Nombre, THEN THE sistema
  SHALL no grabarlo e indicar junto al campo faltante que debe completarlo. [PRD-001 AC-44]
- AC-04 (FR-03): WHEN la operadora graba un presupuesto con Apellido, Nombre y DNI y sin
  Domicilio, Email ni Nro. de Teléfono, THE sistema SHALL grabarlo sin reclamar esos tres campos.
  [PRD-001 AC-45]
- AC-05 (FR-04): WHEN la operadora graba un presupuesto con DNI "23.456.789", THE sistema SHALL
  guardar el DNI 23456789 y mostrarlo así al volver a abrirlo.
- AC-06 (FR-04): IF la operadora graba un presupuesto con DNI "123456", "123456789" o
  "23A56789", THEN THE sistema SHALL no grabarlo e indicar junto al campo DNI que debe tener 7 u 8
  dígitos.
- AC-07 (FR-05): IF la operadora graba un presupuesto con Email "ana@" o "ana@mail", THEN THE
  sistema SHALL no grabarlo e indicar junto al campo Email que debe tener el formato
  usuario@dominio.extensión.
- AC-08 (FR-05): WHEN la operadora graba un presupuesto con Email "ana@mail.com", THE sistema
  SHALL grabarlo con ese Email.
- AC-09 (FR-06): WHEN el último presupuesto grabado tiene el número 154 y la operadora graba uno
  nuevo por primera vez, THE sistema SHALL asignarle el número 155. [PRD-001 AC-06]
- AC-10 (FR-06): WHEN no hay ningún presupuesto grabado y la operadora graba uno nuevo, THE
  sistema SHALL asignarle el número 1.
- AC-11 (FR-07): WHEN la operadora graba un presupuesto por primera vez el 10/03/2026 y lo
  modifica el 12/03/2026, THE sistema SHALL conservar 10/03/2026 como fecha del presupuesto y no
  permitir modificarla.
- AC-12 (FR-08): IF la operadora graba un presupuesto sin ninguna línea, THEN THE sistema SHALL no
  grabarlo e indicar que debe agregar al menos una línea.
- AC-13 (FR-09): WHEN la operadora graba un presupuesto nuevo por primera vez, THE sistema SHALL
  dejarlo en Borrador e indicar ese estado en el mensaje de confirmación. [PRD-001 AC-04]
- AC-14 (FR-10): WHEN la operadora modifica el Domicilio de un presupuesto en Borrador y graba,
  THE sistema SHALL grabar el cambio, mostrar un mensaje de confirmación y mostrar el nuevo
  Domicilio al volver a abrirlo. [PRD-001 AC-07]
- AC-15 (FR-10): WHEN la operadora agrega una línea y quita otra en un presupuesto en Borrador y
  graba, THE sistema SHALL mostrar al volver a abrirlo la línea agregada y no la quitada.
  [PRD-001 AC-24]
- AC-16 (FR-11, FR-12): WHEN la operadora pasa a Final un presupuesto en Borrador y confirma, THE
  sistema SHALL grabarlo en estado Final. [PRD-001 AC-05]
- AC-17 (FR-12): WHEN la operadora pide pasar a Final un presupuesto en Borrador, THE sistema
  SHALL pedir confirmación avisando que después no podrá modificarse, antes de grabar el cambio de
  estado.
- AC-18 (FR-12): IF la operadora cancela la confirmación del paso a Final, THEN THE sistema SHALL
  dejar el presupuesto en Borrador.
- AC-19 (FR-13): IF la operadora intenta modificar cualquier dato o línea de un presupuesto en
  Final, desde la pantalla o con un pedido directo a la API, THEN THE sistema SHALL rechazar el
  cambio y dejar el presupuesto sin modificaciones. [PRD-001 AC-66]
- AC-20 (FR-14): IF la operadora intenta cambiar a Borrador un presupuesto en Final, THEN THE
  sistema SHALL rechazar el cambio y dejarlo en Final. [PRD-001 AC-67]
- AC-21 (FR-15): WHEN la operadora busca "armazon" al cargar una línea, THE sistema SHALL listar
  los artículos activos cuya Descripción contiene "Armazón" o "armazon", y ningún artículo
  inactivo.
- AC-22 (FR-15): WHEN la operadora busca al cargar una línea el Código de un artículo activo, THE
  sistema SHALL listar ese artículo.
- AC-23 (FR-16, FR-17): WHEN la operadora selecciona en una línea un artículo cuyo precio de venta
  guardado es $1.850,00, THE sistema SHALL completar el Código, la Descripción y un Precio Unitario
  de $1.850,00, y no permitir modificar el Precio Unitario. [PRD-001 AC-10, AC-40]
- AC-24 (FR-18): IF la operadora selecciona en una línea un artículo cuyo precio de venta es
  -$150,00, THEN THE sistema SHALL no agregarlo a la línea y mostrar junto al campo el mensaje "El
  precio unitario no puede ser negativo". [PRD-001 AC-12]
- AC-25 (FR-19, FR-26): WHEN la operadora cambia de 1 a 3 la Cantidad y de 0 a 10 el Porcentaje de
  Descuento de una línea con Precio Unitario $1.000,00, THE sistema SHALL mostrar un Precio con
  Descuento de $900,00 y un Precio Final de $2.700,00. [PRD-001 AC-27, AC-35, AC-46]
- AC-26 (FR-20): IF la operadora ingresa como Cantidad 0, -1 o 2,5, THEN THE sistema SHALL no
  aceptar el valor y mostrar junto al campo el mensaje "La cantidad debe ser un número entero mayor
  a 0". [PRD-001 AC-12, AC-43]
- AC-27 (FR-21): IF la operadora ingresa como Porcentaje de Descuento -1 o 100,01, THEN THE sistema
  SHALL no aceptar el valor y mostrar junto al campo el mensaje "El descuento debe estar entre 0 y
  100". [PRD-001 AC-42]
- AC-28 (FR-21, FR-23): WHEN la operadora ingresa un Porcentaje de Descuento de 100 en una línea
  con Precio Unitario $1.000,00, THE sistema SHALL aceptarlo y mostrar un Precio con Descuento de
  $0,00.
- AC-29 (FR-22): WHEN la operadora abre un presupuesto con una línea cargada, THE sistema SHALL
  mostrar para esa línea Código de Artículo, Descripción, Precio Unitario, Cantidad, Porcentaje de
  Descuento, Precio con Descuento y Precio Final. [PRD-001 AC-25]
- AC-30 (FR-23): WHEN la operadora carga un Porcentaje de Descuento de 0 en una línea con Precio
  Unitario $1.000,00, THE sistema SHALL mostrar un Precio con Descuento de $1.000,00.
  [PRD-001 AC-26]
- AC-31 (FR-23, FR-24, NFR-03): WHEN la operadora carga Cantidad 3 y Porcentaje de Descuento 50 en
  una línea con Precio Unitario $6,67, THE sistema SHALL mostrar un Precio con Descuento de $3,34 y
  un Precio Final de $10,02. [PRD-001 AC-29]
- AC-32 (FR-25): WHEN la operadora agrega una línea con Precio Final $2.700,00 a un presupuesto
  cuya única línea tiene Precio Final $10,02 calculado como en AC-31, THE sistema SHALL mostrar un
  total de $2.710,02. [PRD-001 AC-11]
- AC-33 (FR-27): WHEN la operadora abre un presupuesto con líneas, THE sistema SHALL mostrar las
  líneas y el total sin discriminar IVA, con los importes finales al consumidor.
  [PRD-001 AC-28]
- AC-34 (FR-28): WHEN el catálogo se recalcula y un artículo pasa de $1.815,00 a $1.850,00, THE
  sistema SHALL conservar el Precio Unitario de $1.815,00 en la línea de un presupuesto ya grabado
  con ese artículo. [PRD-001 AC-86]
- AC-35 (FR-28): WHEN se desactiva el artículo de una línea de un presupuesto en Borrador, THE
  sistema SHALL mostrar esa línea con su Código, Descripción y Precio Unitario sin cambios al
  volver a abrir el presupuesto.
- AC-36 (NFR-01, FR-06): WHEN dos sesiones graban un presupuesto nuevo al mismo tiempo, THE
  sistema SHALL grabar los dos con números distintos y consecutivos, sin que ninguna sesión reciba
  un error de bloqueo de la base. [PRD-001 AC-63]
- AC-37 (NFR-02): WHEN se ejecutan los criterios de este documento en Chrome y en Edge, en su
  versión estable vigente y en la inmediata anterior, THE sistema SHALL cumplirlos en las 4
  combinaciones. [PRD-001 AC-56]
- AC-38 (NFR-04): WHEN la base tiene 10.000 presupuestos grabados, THE sistema SHALL permitir
  grabar uno nuevo, modificar uno en Borrador y pasar uno a Final con los mismos resultados que en
  AC-01 a AC-36.

## Out of Scope
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
  alcanzados por la Ley 25.326. Mitigación: este PRD no se implementa sin el acceso del PRD 01;
  toda pantalla y operación de presupuestos exige sesión iniciada (PRD 01 FR-01 y NFR-05).
- Riesgo: un presupuesto pasa a Final por error y ya no se puede corregir. Mitigación: confirmación
  previa con aviso (FR-12); el error se corrige con un presupuesto nuevo.
- Riesgo: dos sesiones modifican el mismo presupuesto en Borrador a la vez y una pisa los cambios
  de la otra. PRD-001 no define este caso; queda a resolver en la spec (aviso de conflicto o
  último grabado gana).
- Riesgo: un artículo con precio de venta 0 genera una línea de $0,00. PRD-001 RF-17 solo prohíbe
  precios negativos; el PRD 02 advierte al grabar un costo menor o igual a 0.
- Riesgo: la numeración arranca en 1 y no continúa la de los presupuestos hechos a mano.
  Mitigación: FR-06 define el comienzo en 1; si las dueñas necesitan continuar otra numeración,
  se corrige en este PRD antes de implementarlo.

## Dependencies
- PRD 01 (DISC-001): acceso con sesión iniciada, protección de las operaciones con datos de
  clientes (NFR-05) y mensajes de validación junto al campo (FR-16).
- PRD 02 (DISC-001): artículos con precio de venta guardado, estado activo o inactivo y búsqueda
  por Código o Descripción.
- PRD 05 depende de este: busca por número, fecha y datos del cliente, y genera el PDF solo de
  presupuestos en Final.
- PRD 06 depende de este: factura únicamente presupuestos en Final y envía a ARCA el DNI guardado
  sin puntos (FR-04).
