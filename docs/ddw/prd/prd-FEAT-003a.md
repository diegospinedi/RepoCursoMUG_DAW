# PRD FEAT-003a: Alta del presupuesto con cliente y líneas del catálogo

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-003a (parte a de FEAT-003) |
| Tracker | ninguno |
| Fecha | 2026-11-06 |
| PRD padre | `docs/ddw/prd/prd-FEAT-003.md` (índice), derivado de `prd-DISC-001-04.md` |
| PRD loops | 0 |
| Loops since last human decision | 0 |

> **Trazabilidad.** Cada FR, NFR y AC indica entre corchetes el requisito o criterio de origen. Los AC
> se numeran desde AC-01 en este documento y cada uno cita entre paréntesis su número global en FEAT-003
> (por ejemplo, `FEAT-003 AC-21`), que es el que usa el índice y el mismo que en `prd-DISC-001-04.md`;
> es el mismo criterio que FEAT-001 y FEAT-002. AC-18 y AC-19 son locales a esta parte y no tienen número
> global: validan FR-14 y FR-15, que surgen de decisiones del usuario del 2026-11-06.

> **Alcance (W-PRD-06).** Esta parte tiene 18 AC (17 globales y 1 local), por encima de 5 a 7. El
> usuario lo aprobó explícitamente el 2026-11-06 al elegir la división: el alta tiene que grabar un
> presupuesto completo, con los datos del cliente y sus validaciones, la numeración, el estado inicial y
> al menos una línea tomada del catálogo.

> **Numeración.** El usuario confirmó el 2026-11-06 que la numeración de los presupuestos empieza en 1
> (FR-06), sin continuar la de los presupuestos hechos a mano.

## Context and Problem
Hoy los presupuestos se escriben a mano: se copian los productos y los precios del folleto del proveedor,
lo que genera errores de producto y de precio, y los presupuestos no quedan guardados en ningún lugar
donde se puedan volver a encontrar. Esta parte permite grabar un presupuesto nuevo con los datos del
cliente, un número propio y líneas cuyos artículos y precios salen del catálogo de FEAT-002, con el
precio de venta ya calculado. Mientras no exista FEAT-003b, cada línea se graba con Cantidad 1 y
Porcentaje de Descuento 0.

El presupuesto guarda datos personales del cliente (DNI, domicilio, email y teléfono), alcanzados por la
Ley 25.326 de Protección de Datos Personales.

## Goals
- Que todo presupuesto quede grabado con un número propio y los datos del cliente.
- Que los productos y los precios de las líneas salgan del catálogo, sin transcribirlos a mano.
- Que los datos del cliente que se envían después a ARCA (DNI) y los de contacto queden validados al
  grabar.

## Functional Requirements
- FR-01: El sistema debe permitir grabar un presupuesto nuevo con los datos del cliente Apellido,
  Nombre, DNI, Domicilio, Email y Nro. de Teléfono, guardados dentro del propio presupuesto.
  [FEAT-003 FR-01; PRD-001 RF-01, RF-05, RF-38]
- FR-02: El sistema debe exigir Apellido, Nombre y DNI del cliente para grabar un presupuesto.
  [FEAT-003 FR-02; PRD-001 RF-61]
- FR-03: El sistema debe permitir grabar un presupuesto sin Domicilio, Email ni Nro. de Teléfono del
  cliente. [FEAT-003 FR-03; PRD-001 RF-74]
- FR-04: El sistema debe admitir como DNI únicamente 7 u 8 dígitos, aceptando que se escriban con puntos
  y guardándolo sin ellos. [FEAT-003 FR-04; decisión de las dueñas: el DNI se envía a ARCA en el PRD 06]
- FR-05: El sistema debe admitir en el Email, cuando se cargue, únicamente una dirección con el formato
  usuario@dominio.extensión. [FEAT-003 FR-05; decisión de las dueñas]
- FR-06: El sistema debe asignar a cada presupuesto, al grabarlo por primera vez, un número igual al
  último número asignado más 1, y el número 1 al primer presupuesto. [FEAT-003 FR-06; PRD-001 RF-06]
- FR-07: El sistema debe exigir al menos una línea para grabar un presupuesto.
  [FEAT-003 FR-08; decisión de las dueñas; PRD-001 AC-01, AC-04]
- FR-08: El sistema debe manejar dos estados del presupuesto, Borrador y Final, y dejar en Borrador todo
  presupuesto grabado por primera vez. [FEAT-003 FR-09; PRD-001 RF-04, AC-04]
- FR-09: El sistema debe permitir buscar, al cargar una línea, los artículos activos del catálogo por
  Código o por Descripción, por coincidencia parcial y sin distinguir mayúsculas ni acentos, y no debe
  ofrecer los artículos inactivos. [FEAT-003 FR-15; PRD-001 RF-11; PRD 02 deja la exclusión de los
  inactivos para este PRD]
- FR-10: El sistema debe completar el Código, la Descripción y el Precio Unitario de la línea al
  seleccionarse un artículo, tomando como Precio Unitario el precio de venta guardado en el catálogo, sin
  volver a calcularlo. [FEAT-003 FR-16; PRD-001 RF-39, RF-68, RF-73]
- FR-11: El sistema debe mostrar el Precio Unitario de la línea como un campo de solo lectura.
  [FEAT-003 FR-17; decisión de las dueñas: las líneas salen del catálogo con precio fijo]
- FR-12: El sistema no debe admitir en una línea un artículo cuyo precio de venta sea negativo.
  [FEAT-003 FR-18; PRD-001 RF-17]
- FR-13: El sistema debe registrar y mostrar en cada línea Código de Artículo, Descripción, Precio
  Unitario, Cantidad, Porcentaje de Descuento, Precio con Descuento y Precio Final. En esta parte, la
  Cantidad, el Porcentaje de Descuento y el Precio Final de la línea siguen FR-14. [FEAT-003 FR-22;
  PRD-001 RF-12]
- FR-14: Mientras no exista FEAT-003b, el sistema debe grabar cada línea con Cantidad 1 y Porcentaje de
  Descuento 0, y con un Precio con Descuento y un Precio Final iguales al Precio Unitario. [Decisión del
  usuario 2026-11-06]
- FR-15: El sistema debe asignar a cada presupuesto, al grabarlo por primera vez, la fecha del día como
  fecha del presupuesto y mostrarla al volver a abrirlo; que no se pueda modificar al editarlo se
  verifica en FEAT-003c. [FEAT-003 FR-07, primera parte; decisión del usuario 2026-11-06]

## Non-Functional Requirements
- NFR-01: Todos los criterios de aceptación de este documento deben cumplirse en Chrome y en Edge, en su
  versión estable vigente y en la inmediata anterior (4 combinaciones). [FEAT-003 NFR-02; PRD-001 RNF-03]
- NFR-02: Los importes deben expresarse con 2 decimales, redondeados con mitad hacia arriba.
  [FEAT-003 NFR-03; PRD-001 RF-19]

## Acceptance Criteria
- AC-01 (FR-01; FEAT-003 AC-01): WHEN la operadora carga Apellido, Nombre, DNI, Domicilio, Email y Nro.
  de Teléfono y una línea con un artículo y graba, THE sistema SHALL grabar el presupuesto, mostrar un
  mensaje que confirme que se grabó y mostrar esos seis datos al volver a abrirlo.
  [PRD-001 AC-01, AC-23]
- AC-02 (FR-02; FEAT-003 AC-02): IF la operadora graba un presupuesto con Apellido y Nombre y sin DNI,
  THEN THE sistema SHALL no grabarlo y mostrar junto al campo DNI el mensaje "Ingresá el DNI del
  cliente". [PRD-001 AC-34]
- AC-03 (FR-02; FEAT-003 AC-03): IF la operadora graba un presupuesto sin Apellido o sin Nombre, THEN THE
  sistema SHALL no grabarlo e indicar junto al campo faltante que debe completarlo. [PRD-001 AC-44]
- AC-04 (FR-03; FEAT-003 AC-04): WHEN la operadora graba un presupuesto con Apellido, Nombre y DNI y sin
  Domicilio, Email ni Nro. de Teléfono, THE sistema SHALL grabarlo sin reclamar esos tres campos.
  [PRD-001 AC-45]
- AC-05 (FR-04; FEAT-003 AC-05): WHEN la operadora graba un presupuesto con DNI "23.456.789", THE sistema
  SHALL guardar el DNI 23456789 y mostrarlo así al volver a abrirlo.
- AC-06 (FR-04; FEAT-003 AC-06): IF la operadora graba un presupuesto con DNI "123456", "123456789" o
  "23A56789", THEN THE sistema SHALL no grabarlo e indicar junto al campo DNI que debe tener 7 u 8
  dígitos.
- AC-07 (FR-05; FEAT-003 AC-07): IF la operadora graba un presupuesto con Email "ana@" o "ana@mail", THEN
  THE sistema SHALL no grabarlo e indicar junto al campo Email que debe tener el formato
  usuario@dominio.extensión.
- AC-08 (FR-05; FEAT-003 AC-08): WHEN la operadora graba un presupuesto con Email "ana@mail.com", THE
  sistema SHALL grabarlo con ese Email.
- AC-09 (FR-06; FEAT-003 AC-09): WHEN el último presupuesto grabado tiene el número 154 y la operadora
  graba uno nuevo por primera vez, THE sistema SHALL asignarle el número 155. [PRD-001 AC-06]
- AC-10 (FR-06; FEAT-003 AC-10): WHEN no hay ningún presupuesto grabado y la operadora graba uno nuevo,
  THE sistema SHALL asignarle el número 1.
- AC-11 (FR-07; FEAT-003 AC-12): IF la operadora graba un presupuesto sin ninguna línea, THEN THE
  sistema SHALL no grabarlo e indicar que debe agregar al menos una línea.
- AC-12 (FR-08; FEAT-003 AC-13): WHEN la operadora graba un presupuesto nuevo por primera vez, THE
  sistema SHALL dejarlo en Borrador e indicar ese estado en el mensaje de confirmación. [PRD-001 AC-04]
- AC-13 (FR-09; FEAT-003 AC-21): WHEN la operadora busca "armazon" al cargar una línea, THE sistema SHALL
  listar los artículos activos cuya Descripción contiene "Armazón" o "armazon", y ningún artículo
  inactivo.
- AC-14 (FR-09; FEAT-003 AC-22): WHEN la operadora busca al cargar una línea el Código de un artículo
  activo, THE sistema SHALL listar ese artículo.
- AC-15 (FR-10, FR-11; FEAT-003 AC-23): WHEN la operadora selecciona en una línea un artículo cuyo precio
  de venta guardado es $1.850,00, THE sistema SHALL completar el Código, la Descripción y un Precio
  Unitario de $1.850,00, y no permitir modificar el Precio Unitario. [PRD-001 AC-10, AC-40]
- AC-16 (FR-12; FEAT-003 AC-24): IF la operadora selecciona en una línea un artículo cuyo precio de
  venta es -$150,00, THEN THE sistema SHALL no agregarlo a la línea y mostrar junto al campo el mensaje
  "El precio unitario no puede ser negativo". [PRD-001 AC-12]
- AC-17 (FR-13; FEAT-003 AC-29): WHEN la operadora abre un presupuesto con una línea cargada, THE sistema
  SHALL mostrar para esa línea Código de Artículo, Descripción, Precio Unitario, Cantidad, Porcentaje de
  Descuento, Precio con Descuento y Precio Final. [PRD-001 AC-25]
- AC-18 (FR-14; decisión del usuario): WHEN la operadora agrega a una línea un artículo de $1.850,00 y
  graba, THE sistema SHALL grabar la línea con Cantidad 1, Descuento 0, Precio con Descuento $1.850,00 y
  Precio Final $1.850,00.
- AC-19 (FR-15; decisión del usuario): WHEN la operadora graba un presupuesto por primera vez el
  10/03/2026, THE sistema SHALL guardar 10/03/2026 como fecha del presupuesto y mostrarla al volver a
  abrirlo.

## Out of Scope
- Que la fecha del presupuesto no cambie al modificarlo (FEAT-003 AC-11): corresponde a FEAT-003c.
- Carga y validación de la Cantidad y del Porcentaje de Descuento de la línea, cálculo del Precio con
  Descuento, del Precio Final y del total, recálculo en pantalla e importes con IVA incluido:
  corresponden a FEAT-003b.
- Modificación de un presupuesto en Borrador, paso a Final con confirmación, rechazo de cambios sobre un
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
- Riesgo: acceso no autorizado a datos personales de clientes (DNI, domicilio, email, teléfono),
  alcanzados por la Ley 25.326. Mitigación: esta parte no se implementa sin el acceso de FEAT-001a; toda
  pantalla y operación de presupuestos exige sesión iniciada (PRD 01 FR-01 y NFR-05).
- Riesgo: un artículo con precio de venta 0 genera una línea de $0,00. PRD-001 RF-17 solo prohíbe
  precios negativos; FEAT-002a advierte al grabar un costo menor o igual a 0.
- Riesgo: la numeración arranca en 1 y no continúa la de los presupuestos hechos a mano. Mitigación:
  FR-06 define el comienzo en 1, confirmado por el usuario el 2026-11-06.

## Dependencies
- FEAT-001a: sesión iniciada, protección de las operaciones con datos de clientes y mensajes de
  validación junto al campo (mergeado, PR #2).
- FEAT-002a y FEAT-002b: artículos con precio de venta guardado, estado activo o inactivo y búsqueda por
  Código o Descripción (mergeados, PR #5 y PR #6).
- FEAT-003b, FEAT-003c y FEAT-003d dependen de esta parte. FEAT-003b reemplaza la Cantidad 1 y el
  Porcentaje de Descuento 0 fijos de FR-14.
